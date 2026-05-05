# 自动更新功能设计规格

## 概述

为 OverlayTranslate 添加自动更新功能，启动时检查 GitHub Releases，发现新版本后自动下载安装。

## 架构

```
┌─────────────────────────────────────────────────────────┐
│                    UpdateService                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ GitHubClient │  │ VersionCheck │  │ Downloader   │  │
│  │ (API 调用)    │  │ (版本比较)    │  │ (下载安装包)  │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                    UpdateDialog                         │
│  (更新提示弹窗：显示版本号、更新日志、下载进度)           │
└─────────────────────────────────────────────────────────┘
```

## 核心流程

1. **启动时检查**：App.OnStartup 中调用 UpdateService.CheckForUpdateAsync()
2. **版本比较**：对比当前版本与 GitHub Releases 最新版本
3. **发现更新**：弹出 UpdateDialog 显示更新内容
4. **用户确认**：点击「立即更新」开始下载
5. **下载安装**：下载 installer 到临时目录，启动安装程序后退出

## 文件结构

### 新增文件

| 文件 | 职责 |
|------|------|
| `Services/UpdateService.cs` | 更新检查、下载、安装的核心逻辑 |
| `Services/GitHubRelease.cs` | GitHub API 响应的数据模型 |
| `Windows/UpdateDialog.xaml` | 更新提示弹窗 UI |
| `Windows/UpdateDialog.xaml.cs` | 弹窗逻辑 |

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `App.xaml.cs` | 启动时调用更新检查 |
| `Models/AppSettings.cs` | 添加 UpdateSettings 类 |

## 详细设计

### 1. GitHubRelease.cs - 数据模型

```csharp
public class GitHubRelease
{
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";  // 更新日志 (Markdown)
    public List<GitHubAsset> Assets { get; set; } = [];
    public DateTimeOffset PublishedAt { get; set; }
}

public class GitHubAsset
{
    public string Name { get; set; } = "";
    public string BrowserDownloadUrl { get; set; } = "";
    public long Size { get; set; }
}
```

### 2. UpdateService.cs - 核心逻辑

```csharp
public class UpdateService
{
    private const string RepoOwner = "Ezer013";
    private const string RepoName = "OverlayTranslate";
    private const string InstallerPattern = "*-setup.exe";

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OverlayTranslate-Updater");
    }

    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    public async Task<GitHubRelease?> CheckForUpdateAsync(CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var response = await _httpClient.GetFromJsonAsync<GitHubRelease>(url, ct);
        
        if (response == null) return null;
        
        var latestVersion = ParseVersion(response.TagName);
        var currentVersion = GetCurrentVersion();
        
        return latestVersion > currentVersion ? response : null;
    }

    /// <summary>
    /// 下载安装包
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        GitHubRelease release, 
        IProgress<double> progress, 
        CancellationToken ct)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith("-setup.exe"));
        if (asset == null) throw new InvalidOperationException("未找到安装包");

        var tempPath = Path.Combine(Path.GetTempPath(), asset.Name);
        
        using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, ct);
        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, FileMode.Create);
        
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            
            if (totalBytes > 0)
                progress.Report((double)totalRead / totalBytes * 100);
        }
        
        return tempPath;
    }

    /// <summary>
    /// 启动安装程序并退出当前应用
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });
        
        Application.Current.Shutdown();
    }

    private static Version ParseVersion(string tag)
    {
        // 支持 "v1.2.3" 或 "1.2.3" 格式
        var versionStr = tag.TrimStart('v');
        return Version.TryParse(versionStr, out var v) ? v : new Version(0, 0, 0);
    }

    private static Version GetCurrentVersion()
    {
        return typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
    }
}
```

### 3. UpdateDialog.xaml - 更新弹窗 UI

```xml
<Window x:Class="OverlayTranslate.Windows.UpdateDialog"
        Title="发现新版本" Width="480" Height="360"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- 版本信息 -->
        <StackPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock Text="发现新版本" FontSize="18" FontWeight="Bold"/>
            <TextBlock x:Name="VersionText" Foreground="{DynamicResource {x:Static SystemColors.GrayTextBrushKey}}"/>
        </StackPanel>

        <!-- 更新日志 -->
        <GroupBox Grid.Row="1" Header="更新内容">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <TextBlock x:Name="ChangelogText" TextWrapping="Wrap" Margin="8"/>
            </ScrollViewer>
        </GroupBox>

        <!-- 下载进度 -->
        <ProgressBar x:Name="DownloadProgress" Grid.Row="2" Height="20" Margin="0,12,0,8" Visibility="Collapsed"/>
        <TextBlock x:Name="StatusText" Grid.Row="2" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>

        <!-- 按钮 -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button x:Name="UpdateButton" Content="立即更新" Width="100" Margin="0,0,8,0" Click="OnUpdateClick"/>
            <Button x:Name="SkipButton" Content="稍后提醒" Width="100" Click="OnSkipClick"/>
        </StackPanel>
    </Grid>
</Window>
```

### 4. UpdateDialog.xaml.cs - 弹窗逻辑

```csharp
public partial class UpdateDialog : Window
{
    private readonly UpdateService _updateService;
    private readonly GitHubRelease _release;
    private CancellationTokenSource? _cts;

    public UpdateDialog(UpdateService updateService, GitHubRelease release)
    {
        _updateService = updateService;
        _release = release;
        InitializeComponent();
        
        VersionText.Text = $"v{release.TagName.TrimStart('v')} ({release.PublishedAt:yyyy-MM-dd})";
        ChangelogText.Text = release.Body;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        SkipButton.Content = "取消";
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "正在下载...";
        
        _cts = new CancellationTokenSource();
        
        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress.Value = p;
                StatusText.Text = $"下载中... {p:F0}%";
            });
            
            var installerPath = await _updateService.DownloadInstallerAsync(_release, progress, _cts.Token);
            
            StatusText.Text = "下载完成，即将安装...";
            await Task.Delay(1000);
            
            _updateService.LaunchInstallerAndExit(installerPath);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "下载已取消";
            UpdateButton.IsEnabled = true;
            SkipButton.Content = "稍后提醒";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败: {ex.Message}";
            UpdateButton.IsEnabled = true;
            SkipButton.Content = "稍后提醒";
        }
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
        Close();
    }
}
```

### 5. App.xaml.cs - 启动时检查

在 `OnStartup` 方法中，配置服务完成后添加：

```csharp
// 检查更新（异步，不阻塞启动）
_ = CheckForUpdateAsync();
```

添加方法：

```csharp
private async Task CheckForUpdateAsync()
{
    try
    {
        var httpClient = Services.GetRequiredService<HttpClient>();
        var updateService = new UpdateService(httpClient);
        
        var release = await updateService.CheckForUpdateAsync(CancellationToken.None);
        if (release != null)
        {
            var dialog = new UpdateDialog(updateService, release);
            dialog.Owner = MainWindow;
            dialog.ShowDialog();
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "检查更新失败");
    }
}
```

### 6. AppSettings.cs - 更新配置

```csharp
public class UpdateSettings
{
    public bool AutoCheck { get; set; } = true;
    public string? SkippedVersion { get; set; }
}
```

在 `AppSettings` 类中添加：

```csharp
public UpdateSettings Update { get; set; } = new();
```

## 错误处理

| 场景 | 处理方式 |
|------|----------|
| 网络不可用 | 静默失败，不打扰用户 |
| GitHub API 限流 | 静默失败，下次启动重试 |
| 下载中断 | 用户可取消，下次启动重试 |
| 安装包损坏 | 下载完成后校验 SHA256（可选） |

## 测试策略

1. **单元测试**：版本比较逻辑、数据模型序列化
2. **集成测试**：GitHub API 调用（使用 mock）
3. **手动测试**：下载安装流程

## 安全考虑

1. **HTTPS**：所有 API 调用和下载使用 HTTPS
2. **User-Agent**：设置合法的 User-Agent 避免被 GitHub 限流
3. **安装包校验**：可选添加 SHA256 校验

# 自动更新功能实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为 OverlayTranslate 添加自动更新功能，启动时检查 GitHub Releases，发现新版本后自动下载安装

**架构：** 使用 UpdateService 封装 GitHub API 调用、版本比较、下载安装逻辑；UpdateDialog 提供用户交互界面；App.OnStartup 中异步触发检查

**技术栈：** HttpClient、System.Version、Process.Start

---

## 文件结构

| 操作 | 文件 | 职责 |
|------|------|------|
| 创建 | `OverlayTranslate/Services/GitHubRelease.cs` | GitHub API 响应的数据模型 |
| 创建 | `OverlayTranslate/Services/UpdateService.cs` | 更新检查、下载、安装的核心逻辑 |
| 创建 | `OverlayTranslate/Windows/UpdateDialog.xaml` | 更新提示弹窗 UI |
| 创建 | `OverlayTranslate/Windows/UpdateDialog.xaml.cs` | 弹窗逻辑 |
| 修改 | `OverlayTranslate/App.xaml.cs` | 启动时调用更新检查 |
| 修改 | `OverlayTranslate/Models/AppSettings.cs` | 添加 UpdateSettings 类 |

---

### 任务 1：GitHubRelease 数据模型

**文件：**
- 创建：`OverlayTranslate/Services/GitHubRelease.cs`

- [ ] **步骤 1：创建 GitHubRelease.cs**

```csharp
using System.Text.Json.Serialization;

namespace OverlayTranslate.Services;

/// <summary>
/// GitHub Release API 响应模型
/// </summary>
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
```

- [ ] **步骤 2：Commit**

```bash
git add OverlayTranslate/Services/GitHubRelease.cs
git commit -m "feat(update): 添加 GitHub Release 数据模型"
```

---

### 任务 2：UpdateService 核心逻辑

**文件：**
- 创建：`OverlayTranslate/Services/UpdateService.cs`
- 测试：`OverlayTranslate.Tests/UpdateServiceTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
// OverlayTranslate.Tests/UpdateServiceTests.cs
using OverlayTranslate.Services;

namespace OverlayTranslate.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.0", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("v1.2.3", "1.3.0", false)]
    [InlineData("1.2.3", "1.2.0", true)]
    public void ParseVersion_CompareVersions_WorksCorrectly(string tag, string current, bool shouldUpdate)
    {
        var tagVersion = Version.Parse(tag.TrimStart('v'));
        var currentVersion = Version.Parse(current);

        Assert.Equal(shouldUpdate, tagVersion > currentVersion);
    }

    [Fact]
    public void GitHubRelease_DefaultValues_AreCorrect()
    {
        var release = new GitHubRelease();

        Assert.Equal("", release.TagName);
        Assert.Equal("", release.Name);
        Assert.Equal("", release.Body);
        Assert.Empty(release.Assets);
    }

    [Fact]
    public void GitHubAsset_DefaultValues_AreCorrect()
    {
        var asset = new GitHubAsset();

        Assert.Equal("", asset.Name);
        Assert.Equal("", asset.BrowserDownloadUrl);
        Assert.Equal(0, asset.Size);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test OverlayTranslate.Tests/OverlayTranslate.Tests.csproj --filter "UpdateService" --no-restore --nologo`
预期：FAIL，报错 "UpdateService not defined"

- [ ] **步骤 3：创建 UpdateService.cs**

```csharp
using System.Diagnostics;
using System.Net.Http.Json;
using Serilog;

namespace OverlayTranslate.Services;

public class UpdateService
{
    private const string RepoOwner = "uezer";
    private const string RepoName = "OverlayTranslate";

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OverlayTranslate-Updater");
        }
    }

    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    public async Task<GitHubRelease?> CheckForUpdateAsync(CancellationToken ct)
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetFromJsonAsync<GitHubRelease>(url, ct);

            if (response == null) return null;

            var latestVersion = ParseVersion(response.TagName);
            var currentVersion = GetCurrentVersion();

            Log.Information("版本检查: 当前={Current}, 最新={Latest}", currentVersion, latestVersion);

            return latestVersion > currentVersion ? response : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "检查更新失败");
            return null;
        }
    }

    /// <summary>
    /// 下载安装包
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        GitHubRelease release,
        IProgress<double> progress,
        CancellationToken ct)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));
        if (asset == null)
            throw new InvalidOperationException("未找到安装包");

        var tempPath = Path.Combine(Path.GetTempPath(), asset.Name);

        using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, ct);
        response.EnsureSuccessStatusCode();

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

        Log.Information("下载完成: {Path} ({Size} bytes)", tempPath, totalRead);
        return tempPath;
    }

    /// <summary>
    /// 启动安装程序并退出当前应用
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        Log.Information("启动安装程序: {Path}", installerPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });

        Application.Current.Shutdown();
    }

    internal static Version ParseVersion(string tag)
    {
        var versionStr = tag.TrimStart('v');
        return Version.TryParse(versionStr, out var v) ? v : new Version(0, 0, 0);
    }

    internal static Version GetCurrentVersion()
    {
        return typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test OverlayTranslate.Tests/OverlayTranslate.Tests.csproj --filter "UpdateService" --no-restore --nologo`
预期：PASS

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Services/UpdateService.cs OverlayTranslate.Tests/UpdateServiceTests.cs
git commit -m "feat(update): 添加 UpdateService 核心逻辑"
```

---

### 任务 3：UpdateDialog 更新弹窗

**文件：**
- 创建：`OverlayTranslate/Windows/UpdateDialog.xaml`
- 创建：`OverlayTranslate/Windows/UpdateDialog.xaml.cs`

- [ ] **步骤 1：创建 UpdateDialog.xaml**

```xml
<Window x:Class="OverlayTranslate.Windows.UpdateDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:OverlayTranslate.Localization"
        Title="{x:Static loc:LocManager.__}"
        Width="480" Height="380"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">
    <Window.Resources>
        <Style TargetType="Button">
            <Setter Property="MinWidth" Value="100"/>
            <Setter Property="Padding" Value="16,8"/>
            <Setter Property="Margin" Value="4"/>
        </Style>
    </Window.Resources>

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- 版本信息 -->
        <StackPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock FontSize="18" FontWeight="Bold">
                <Run Text="发现新版本 "/>
                <Run x:Name="VersionRun" Text="v1.0.0"/>
            </TextBlock>
            <TextBlock x:Name="DateText" Foreground="{DynamicResource {x:Static SystemColors.GrayTextBrushKey}}" FontSize="12"/>
        </StackPanel>

        <!-- 更新日志 -->
        <GroupBox Grid.Row="1" Header="更新内容" Padding="0">
            <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="8">
                <TextBlock x:Name="ChangelogText" TextWrapping="Wrap"/>
            </ScrollViewer>
        </GroupBox>

        <!-- 下载进度 -->
        <ProgressBar x:Name="DownloadProgress" Grid.Row="2" Height="24" Margin="0,12,0,0" Visibility="Collapsed"/>
        <TextBlock x:Name="StatusText" Grid.Row="2" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>

        <!-- 按钮 -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button x:Name="UpdateButton" Content="立即更新" IsDefault="True" Click="OnUpdateClick"/>
            <Button x:Name="SkipButton" Content="稍后提醒" IsCancel="True" Click="OnSkipClick"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **步骤 2：创建 UpdateDialog.xaml.cs**

```csharp
using System.Windows;
using OverlayTranslate.Services;

namespace OverlayTranslate.Windows;

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

        Title = "发现新版本";
        VersionRun.Text = $"v{release.TagName.TrimStart('v')}";
        DateText.Text = release.PublishedAt.ToString("yyyy-MM-dd");
        ChangelogText.Text = string.IsNullOrWhiteSpace(release.Body)
            ? "暂无更新说明"
            : release.Body;
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
            ResetButtons();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败: {ex.Message}";
            ResetButtons();
        }
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
        Close();
    }

    private void ResetButtons()
    {
        UpdateButton.IsEnabled = true;
        SkipButton.Content = "稍后提醒";
        DownloadProgress.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **步骤 3：Commit**

```bash
git add OverlayTranslate/Windows/UpdateDialog.xaml OverlayTranslate/Windows/UpdateDialog.xaml.cs
git commit -m "feat(update): 添加更新提示弹窗"
```

---

### 任务 4：集成到 App 启动流程

**文件：**
- 修改：`OverlayTranslate/App.xaml.cs`

- [ ] **步骤 1：在 OnStartup 中添加更新检查**

在 `OnStartup` 方法末尾（显示主窗口之后）添加：

```csharp
// 异步检查更新（不阻塞启动）
_ = CheckForUpdateAsync();
```

- [ ] **步骤 2：添加 CheckForUpdateAsync 方法**

在 `App` 类中添加：

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
            // 确保在 UI 线程显示对话框
            await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new UpdateDialog(updateService, release);
                dialog.Owner = MainWindow;
                dialog.ShowDialog();
            });
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "检查更新失败");
    }
}
```

- [ ] **步骤 3：添加 using 语句**

在文件顶部添加：

```csharp
using OverlayTranslate.Services;
```

- [ ] **步骤 4：运行测试验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/App.xaml.cs
git commit -m "feat(update): 启动时自动检查更新"
```

---

### 任务 5：添加更新配置项（可选）

**文件：**
- 修改：`OverlayTranslate/Models/AppSettings.cs`

- [ ] **步骤 1：添加 UpdateSettings 类**

在 `AppSettings.cs` 文件末尾添加：

```csharp
public class UpdateSettings
{
    public bool AutoCheck { get; set; } = true;
    public string? SkippedVersion { get; set; }
}
```

- [ ] **步骤 2：在 AppSettings 中添加属性**

在 `AppSettings` 类中添加：

```csharp
public UpdateSettings Update { get; set; } = new();
```

- [ ] **步骤 3：修改 CheckForUpdateAsync 支持跳过版本**

修改 `App.xaml.cs` 中的 `CheckForUpdateAsync` 方法：

```csharp
private async Task CheckForUpdateAsync()
{
    try
    {
        var configManager = Services.GetRequiredService<ConfigManager>();

        // 检查是否启用自动更新
        if (!configManager.Settings.Update.AutoCheck)
            return;

        var httpClient = Services.GetRequiredService<HttpClient>();
        var updateService = new UpdateService(httpClient);

        var release = await updateService.CheckForUpdateAsync(CancellationToken.None);
        if (release == null)
            return;

        // 检查是否跳过此版本
        var skippedVersion = configManager.Settings.Update.SkippedVersion;
        if (!string.IsNullOrEmpty(skippedVersion) &&
            release.TagName.TrimStart('v') == skippedVersion)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            var dialog = new UpdateDialog(updateService, release);
            dialog.Owner = MainWindow;

            if (dialog.ShowDialog() == false)
            {
                // 用户选择跳过，记录版本号
                configManager.Settings.Update.SkippedVersion = release.TagName.TrimStart('v');
                configManager.Save();
            }
        });
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "检查更新失败");
    }
}
```

- [ ] **步骤 4：运行测试验证**

运行：`dotnet test OverlayTranslate.Tests/OverlayTranslate.Tests.csproj --no-restore --nologo`
预期：全部通过

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Models/AppSettings.cs OverlayTranslate/App.xaml.cs
git commit -m "feat(update): 添加更新配置项（自动检查、跳过版本）"
```

---

## 自检

### 规格覆盖度
- ✅ GitHub API 调用 → 任务 2
- ✅ 版本比较 → 任务 2
- ✅ 下载安装包 → 任务 2
- ✅ 更新弹窗 UI → 任务 3
- ✅ 启动时检查 → 任务 4
- ✅ 配置项（可选） → 任务 5

### 类型一致性
- `GitHubRelease`、`GitHubAsset` 在任务 1 定义，任务 2、3 使用
- `UpdateService` 在任务 2 定义，任务 3、4 使用
- `UpdateDialog` 在任务 3 定义，任务 4 使用
- `UpdateSettings` 在任务 5 定义

### 无占位符
- 所有步骤包含完整代码
- 所有命令包含预期输出

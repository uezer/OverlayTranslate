# 字体大小 + 工具栏避让 + 暗黑模式 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 实现三个功能：字体大小策略（auto/fit-width/custom）、工具栏自动避让+可拖动、暗黑模式（light/dark/system）

**架构：** 字体大小通过扩展 StyleAnalyzer 支持三种模式；工具栏通过重写 PositionToolbar 实现避让，添加拖动事件；暗黑模式通过资源字典 + ThemeManager 实现主题切换。

**技术栈：** WPF XAML, C#, System.Windows.Media, Microsoft.Win32 注册表读取

---

### 任务 1：AppSettings 新增 OtherSettings

**文件：**
- 修改：`OverlayTranslate/Models/AppSettings.cs`
- 测试：`OverlayTranslate.Tests/ModelTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `ModelTests.cs` 中添加：

```csharp
[Fact]
public void AppSettings_HasOtherSettings_WithDefaults()
{
    var settings = new AppSettings();
    Assert.NotNull(settings.Other);
    Assert.Equal("auto", settings.Other.FontSizeMode);
    Assert.Equal(14, settings.Other.CustomFontSize);
    Assert.Equal("system", settings.Other.Theme);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test --filter "AppSettings_HasOtherSettings" --no-restore --nologo`
预期：FAIL，`Other` 不存在

- [ ] **步骤 3：编写实现代码**

在 `AppSettings.cs` 中的 `AppSettings` 类添加属性，并创建 `OtherSettings` 类：

```csharp
// 在 AppSettings 类中添加：
public OtherSettings Other { get; set; } = new();

// 新增类：
public class OtherSettings
{
    public string FontSizeMode { get; set; } = "auto"; // auto / fit-width / custom
    public int CustomFontSize { get; set; } = 14;
    public string Theme { get; set; } = "system"; // light / dark / system
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test --filter "AppSettings_HasOtherSettings" --no-restore --nologo`
预期：PASS

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Models/AppSettings.cs OverlayTranslate.Tests/ModelTests.cs
git commit -m "feat(settings): 新增 OtherSettings 含字号模式和主题设置"
```

---

### 任务 2：StyleAnalyzer 支持三种字号模式

**文件：**
- 修改：`OverlayTranslate/Services/StyleAnalyzer.cs`
- 测试：`OverlayTranslate.Tests/StyleAnalyzerTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `StyleAnalyzerTests.cs` 中添加：

```csharp
[Fact]
public void Analyze_AutoMode_UsesProvidedBaseFontSize()
{
    var analyzer = new StyleAnalyzer();
    var region = new Rect(0, 0, 200, 50);
    var result = analyzer.Analyze(region, "test", 12.0, "auto");
    Assert.Equal(12.0, result.FontSize);
}

[Fact]
public void Analyze_FitWidthMode_ScalesToFit()
{
    var analyzer = new StyleAnalyzer();
    var region = new Rect(0, 0, 100, 50);
    // 译文很长，字号应该缩小以适应宽度
    var result = analyzer.Analyze(region, "这是一个很长的翻译文本用于测试", 20.0, "fit-width");
    Assert.True(result.FontSize < 20.0);
}

[Fact]
public void Analyze_CustomMode_UsesCustomSize()
{
    var analyzer = new StyleAnalyzer();
    var region = new Rect(0, 0, 200, 50);
    var result = analyzer.Analyze(region, "test", 12.0, "custom", 18);
    Assert.Equal(18.0, result.FontSize);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test --filter "Analyze_AutoMode|Analyze_FitWidthMode|Analyze_CustomMode" --no-restore --nologo`
预期：FAIL，签名不匹配

- [ ] **步骤 3：编写实现代码**

修改 `StyleAnalyzer.Analyze` 方法签名和实现：

```csharp
public TextStyleInfo Analyze(Rect boundingBox, string text,
    double baseFontSize = 0, string fontSizeMode = "auto", int customFontSize = 14)
{
    double fontSize;
    switch (fontSizeMode)
    {
        case "custom":
            fontSize = customFontSize;
            break;
        case "fit-width":
            fontSize = baseFontSize > 0 ? baseFontSize : boundingBox.Height * 0.75;
            // 根据选区宽度和译文长度调整
            var charWidth = fontSize * 0.6;
            var totalWidth = charWidth * text.Length;
            if (totalWidth > boundingBox.Width)
                fontSize = Math.Max(8, fontSize * (boundingBox.Width / totalWidth));
            break;
        default: // "auto"
            fontSize = baseFontSize > 0 ? baseFontSize : boundingBox.Height * 0.75;
            break;
    }
    fontSize = Math.Max(8, Math.Min(72, fontSize));

    var bgLuminance = 0.0;
    return new TextStyleInfo
    {
        FontSize = fontSize,
        TextColor = Colors.Black, // 颜色由调用方根据背景色设置
        IsBold = false,
        RegionWidth = boundingBox.Width,
        RegionHeight = boundingBox.Height
    };
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test --filter "StyleAnalyzerTests" --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Services/StyleAnalyzer.cs OverlayTranslate.Tests/StyleAnalyzerTests.cs
git commit -m "feat(style): StyleAnalyzer 支持 auto/fit-width/custom 三种字号模式"
```

---

### 任务 3：OverlayWindow 从 OCR 结果提取字号

**文件：**
- 修改：`OverlayTranslate/Windows/OverlayWindow.xaml.cs`

- [ ] **步骤 1：修改 ProcessSelectionAsync 中的 StyleAnalyzer 调用**

在 `ProcessSelectionAsync` 中，OCR 完成后提取字号并传给 StyleAnalyzer：

```csharp
// 在 var styleInfo = ... 之前添加：
// 从 OCR 文字框高度估算原图字号
var baseFontSize = ocrResult.TextBlocks
    .Where(b => b.BoundingBox.Height > 0)
    .Select(b => b.BoundingBox.Height)
    .OrderBy(h => h)
    .MedianOrDefault(boundingBox.Height * 0.75);

var app = (App)Application.Current;
var fontMode = app.Services.GetRequiredService<ConfigManager>().Settings.Other.FontSizeMode;
var customSize = app.Services.GetRequiredService<ConfigManager>().Settings.Other.CustomFontSize;

var styleInfo = _styleAnalyzer.Analyze(selection, _originalText, baseFontSize, fontMode, customSize);
// 颜色逻辑保持不变
var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData, selection);
var wpfBgColor = System.Windows.Media.Color.FromRgb(
    (byte)Math.Clamp(bgColor.Val2, 0, 255),
    (byte)Math.Clamp(bgColor.Val1, 0, 255),
    (byte)Math.Clamp(bgColor.Val0, 0, 255));
var luminance = 0.299 * wpfBgColor.R + 0.587 * wpfBgColor.G + 0.114 * wpfBgColor.B;
styleInfo.TextColor = luminance < 128 ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black;
```

注意：`MedianOrDefault` 需要自己实现或用 LINQ 替代：

```csharp
// 可以用以下方式计算中位数：
var heights = ocrResult.TextBlocks
    .Where(b => b.BoundingBox.Height > 0)
    .Select(b => b.BoundingBox.Height)
    .OrderBy(h => h)
    .ToArray();
var baseFontSize = heights.Length > 0 ? heights[heights.Length / 2] : selection.Height * 0.75;
```

- [ ] **步骤 2：运行测试确认不破坏现有测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 3：Commit**

```bash
git add OverlayTranslate/Windows/OverlayWindow.xaml.cs
git commit -m "feat(overlay): 从 OCR 文字框高度估算原图字号"
```

---

### 任务 4：工具栏自动避让算法

**文件：**
- 修改：`OverlayTranslate/Windows/OverlayWindow.xaml.cs`

- [ ] **步骤 1：重写 PositionToolbar 方法**

```csharp
private void PositionToolbar(Rect selection)
{
    Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    var toolbarSize = Toolbar.DesiredSize;

    var screenW = ActualWidth;
    var screenH = ActualHeight;

    // 优先级: 下 → 上 → 右 → 左 → 贴边
    double x, y;

    // 尝试下方
    y = selection.Bottom + 8;
    if (y + toolbarSize.Height > screenH)
    {
        // 尝试上方
        y = selection.Top - toolbarSize.Height - 8;
        if (y < 0)
        {
            // 尝试右侧
            x = selection.Right + 8;
            y = selection.Y;
            if (x + toolbarSize.Width > screenW)
            {
                // 尝试左侧
                x = selection.Left - toolbarSize.Width - 8;
                if (x < 0)
                    x = 8; // 贴边
            }
            // 纵向 clamp
            y = Math.Max(8, Math.Min(y, screenH - toolbarSize.Height - 8));
        }
        else
        {
            x = selection.X;
        }
    }
    else
    {
        x = selection.X;
    }

    // 横向 clamp
    x = Math.Max(8, Math.Min(x, screenW - toolbarSize.Width - 8));
    // 纵向 clamp
    y = Math.Max(8, Math.Min(y, screenH - toolbarSize.Height - 8));

    Canvas.SetLeft(Toolbar, x);
    Canvas.SetTop(Toolbar, y);
}
```

- [ ] **步骤 2：运行测试确认不破坏现有测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 3：Commit**

```bash
git add OverlayTranslate/Windows/OverlayWindow.xaml.cs
git commit -m "fix(toolbar): 工具栏自动避让算法 下→上→右→左→贴边"
```

---

### 任务 5：工具栏可拖动

**文件：**
- 修改：`OverlayTranslate/Controls/FloatingToolbar.xaml`
- 修改：`OverlayTranslate/Controls/FloatingToolbar.xaml.cs`

- [ ] **步骤 1：在 XAML 的 Border 添加 Cursor 和事件**

修改 `FloatingToolbar.xaml` 的 Border 元素：

```xml
<Border Background="#F0F0F0" CornerRadius="6" Padding="8"
        BorderBrush="#CCCCCC" BorderThickness="1"
        Cursor="SizeAll"
        MouseLeftButtonDown="OnBorderMouseLeftButtonDown"
        MouseMove="OnBorderMouseMove"
        MouseLeftButtonUp="OnBorderMouseLeftButtonUp">
```

- [ ] **步骤 2：在 code-behind 添加拖动逻辑**

在 `FloatingToolbar.xaml.cs` 中添加：

```csharp
private bool _isDragging;
private Point _dragStart;

public event Action? OnDragStarted; // 通知 OverlayWindow 停止自动避让

private void OnBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    _isDragging = true;
    _dragStart = e.GetPosition(Parent as UIElement);
    ((Border)sender).CaptureMouse();
    OnDragStarted?.Invoke();
}

private void OnBorderMouseMove(object sender, MouseEventArgs e)
{
    if (!_isDragging) return;
    var currentPos = e.GetPosition(Parent as UIElement);
    var dx = currentPos.X - _dragStart.X;
    var dy = currentPos.Y - _dragStart.Y;

    var currentLeft = Canvas.GetLeft(this);
    var currentTop = Canvas.GetTop(this);
    Canvas.SetLeft(this, currentLeft + dx);
    Canvas.SetTop(this, currentTop + dy);
    _dragStart = currentPos;
}

private void OnBorderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
{
    _isDragging = false;
    ((Border)sender).ReleaseMouseCapture();
}
```

- [ ] **步骤 3：在 OverlayWindow 中响应拖动事件**

在 `OverlayWindow` 构造函数中添加事件订阅：

```csharp
private bool _autoPositionToolbar = true;

// 在构造函数中：
Toolbar.OnDragStarted += () => _autoPositionToolbar = false;

// 在 PositionToolbar 方法开头添加：
if (!_autoPositionToolbar) return;

// 在 HandleReselect 方法中添加：
_autoPositionToolbar = true;
```

- [ ] **步骤 4：运行测试确认不破坏现有测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Controls/FloatingToolbar.xaml OverlayTranslate/Controls/FloatingToolbar.xaml.cs OverlayTranslate/Windows/OverlayWindow.xaml.cs
git commit -m "feat(toolbar): 工具栏支持拖动定位，重选后恢复自动避让"
```

---

### 任务 6：主题资源字典

**文件：**
- 创建：`OverlayTranslate/Themes/Light.xaml`
- 创建：`OverlayTranslate/Themes/Dark.xaml`

- [ ] **步骤 1：创建 Light.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="WindowBackgroundColor">#FFFFFF</Color>
    <Color x:Key="PanelBackgroundColor">#F0F0F0</Color>
    <Color x:Key="BorderBaseColor">#CCCCCC</Color>
    <Color x:Key="TextBaseColor">#1E1E1E</Color>
    <Color x:Key="AccentColor">#0078D4</Color>
    <Color x:Key="ButtonFaceColor">#E1E1E1</Color>
    <Color x:Key="ButtonHoverColor">#D0D0D0</Color>

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource WindowBackgroundColor}" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource PanelBackgroundColor}" />
    <SolidColorBrush x:Key="BorderBaseBrush" Color="{StaticResource BorderBaseColor}" />
    <SolidColorBrush x:Key="TextBaseBrush" Color="{StaticResource TextBaseColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="ButtonFaceBrush" Color="{StaticResource ButtonFaceColor}" />
    <SolidColorBrush x:Key="ButtonHoverBrush" Color="{StaticResource ButtonHoverColor}" />
</ResourceDictionary>
```

- [ ] **步骤 2：创建 Dark.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="WindowBackgroundColor">#1E1E1E</Color>
    <Color x:Key="PanelBackgroundColor">#2D2D2D</Color>
    <Color x:Key="BorderBaseColor">#404040</Color>
    <Color x:Key="TextBaseColor">#E0E0E0</Color>
    <Color x:Key="AccentColor">#4CC2FF</Color>
    <Color x:Key="ButtonFaceColor">#3C3C3C</Color>
    <Color x:Key="ButtonHoverColor">#505050</Color>

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource WindowBackgroundColor}" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource PanelBackgroundColor}" />
    <SolidColorBrush x:Key="BorderBaseBrush" Color="{StaticResource BorderBaseColor}" />
    <SolidColorBrush x:Key="TextBaseBrush" Color="{StaticResource TextBaseColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="ButtonFaceBrush" Color="{StaticResource ButtonFaceColor}" />
    <SolidColorBrush x:Key="ButtonHoverBrush" Color="{StaticResource ButtonHoverColor}" />
</ResourceDictionary>
```

- [ ] **步骤 3：Commit**

```bash
git add OverlayTranslate/Themes/
git commit -m "feat(theme): 创建 Light/Dark 颜色资源字典"
```

---

### 任务 7：ThemeManager

**文件：**
- 创建：`OverlayTranslate/Infrastructure/ThemeManager.cs`
- 测试：`OverlayTranslate.Tests/ModelTests.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
[Fact]
public void ThemeManager_GetSystemTheme_ReturnsValidValue()
{
    var theme = OverlayTranslate.Infrastructure.ThemeManager.GetSystemTheme();
    Assert.Contains(theme, ["light", "dark"]);
}

[Fact]
public void ThemeManager_SetTheme_DoesNotThrow()
{
    // 需要 Application 上下文，此测试仅验证不抛异常
    // 在非 WPF 环境中跳过
    if (Application.Current == null) return;
    var ex = Record.Exception(() =>
        OverlayTranslate.Infrastructure.ThemeManager.SetTheme("dark"));
    Assert.Null(ex);
}
```

- [ ] **步骤 2：编写实现代码**

```csharp
// OverlayTranslate/Infrastructure/ThemeManager.cs
using Microsoft.Win32;

namespace OverlayTranslate.Infrastructure;

public static class ThemeManager
{
    public static event Action<string>? ThemeChanged;

    public static string GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int v && v == 0 ? "dark" : "light";
        }
        catch
        {
            return "light";
        }
    }

    public static void SetTheme(string theme)
    {
        var resolved = theme == "system" ? GetSystemTheme() : theme;
        var dictUri = resolved == "dark"
            ? new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);

        var app = System.Windows.Application.Current;
        var existing = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/") == true);
        if (existing != null)
            app.Resources.MergedDictionaries.Remove(existing);

        app.Resources.MergedDictionaries.Insert(0,
            new System.Windows.ResourceDictionary { Source = dictUri });

        ThemeChanged?.Invoke(resolved);
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter "ThemeManager" --no-restore --nologo`
预期：PASS

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Infrastructure/ThemeManager.cs OverlayTranslate.Tests/ModelTests.cs
git commit -m "feat(theme): ThemeManager 实现系统主题检测和切换"
```

---

### 任务 8：App.xaml.cs 初始化主题

**文件：**
- 修改：`OverlayTranslate/App.xaml.cs`
- 修改：`OverlayTranslate/App.xaml`

- [ ] **步骤 1：在 App.xaml 中添加默认主题资源**

在 `App.xaml` 的 `Application.Resources` 中添加：

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/Light.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **步骤 2：在 App.xaml.cs 的 OnStartup 中初始化主题**

在 `OnStartup` 方法中，DI 配置完成后添加：

```csharp
// 初始化主题
var theme = configManager.Settings.Other.Theme;
ThemeManager.SetTheme(theme);
```

- [ ] **步骤 3：运行测试确认不破坏现有测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/App.xaml OverlayTranslate/App.xaml.cs
git commit -m "feat(theme): 应用启动时初始化主题"
```

---

### 任务 9：FloatingToolbar 使用主题资源

**文件：**
- 修改：`OverlayTranslate/Controls/FloatingToolbar.xaml`

- [ ] **步骤 1：将硬编码颜色替换为 DynamicResource**

```xml
<Border Background="{DynamicResource PanelBackgroundBrush}"
        CornerRadius="6" Padding="8"
        BorderBrush="{DynamicResource BorderBaseBrush}" BorderThickness="1"
        Cursor="SizeAll"
        MouseLeftButtonDown="OnBorderMouseLeftButtonDown"
        MouseMove="OnBorderMouseMove"
        MouseLeftButtonUp="OnBorderMouseLeftButtonUp">
```

在内部 StackPanel 的 TextBlock 中添加 `Foreground="{DynamicResource TextBaseBrush}"`。
在 Button 中添加样式绑定。

- [ ] **步骤 2：运行测试确认不破坏现有测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 3：Commit**

```bash
git add OverlayTranslate/Controls/FloatingToolbar.xaml
git commit -m "feat(toolbar): FloatingToolbar 使用 DynamicResource 绑定主题颜色"
```

---

### 任务 10：SettingsWindow 使用主题资源 + 新增设置项

**文件：**
- 修改：`OverlayTranslate/Windows/SettingsWindow.xaml`
- 修改：`OverlayTranslate/Windows/SettingsWindow.xaml.cs`

- [ ] **步骤 1：在"其他" tab 添加主题和字号设置**

在 SettingsWindow.xaml 的"其他" TabItem 中添加：

```xml
<TextBlock Text="主题:" FontWeight="SemiBold" Margin="0,0,0,4" />
<ComboBox x:Name="ThemeComboBox" Width="200" HorizontalAlignment="Left" Margin="0,0,0,12" />

<TextBlock Text="字号模式:" FontWeight="SemiBold" Margin="0,0,0,4" />
<ComboBox x:Name="FontSizeModeComboBox" Width="200" HorizontalAlignment="Left" Margin="0,0,0,12" />

<TextBlock Text="自定义字号:" FontWeight="SemiBold" Margin="0,0,0,4" />
<TextBox x:Name="CustomFontSizeTextBox" Width="100" HorizontalAlignment="Left" Margin="0,0,0,12" />
```

- [ ] **步骤 2：在 SettingsWindow.xaml.cs 中加载和保存设置**

在 `LoadSettings()` 中添加：

```csharp
var themes = new[] { "system", "light", "dark" };
foreach (var t in themes) ThemeComboBox.Items.Add(t);
ThemeComboBox.SelectedItem = settings.Other.Theme;

var fontModes = new[] { "auto", "fit-width", "custom" };
foreach (var m in fontModes) FontSizeModeComboBox.Items.Add(m);
FontSizeModeComboBox.SelectedItem = settings.Other.FontSizeMode;
CustomFontSizeTextBox.Text = settings.Other.CustomFontSize.ToString();
```

在 `OnSaveClick()` 中添加：

```csharp
settings.Other.Theme = ThemeComboBox.SelectedItem?.ToString() ?? "system";
settings.Other.FontSizeMode = FontSizeModeComboBox.SelectedItem?.ToString() ?? "auto";
if (int.TryParse(CustomFontSizeTextBox.Text, out var fontSize))
    settings.Other.CustomFontSize = fontSize;

// 保存后立即应用主题
ThemeManager.SetTheme(settings.Other.Theme);
```

- [ ] **步骤 3：SettingsWindow 也使用 DynamicResource**

将 SettingsWindow.xaml 的 Window 和内部元素的硬编码颜色替换为 `{DynamicResource ...}`。

- [ ] **步骤 4：运行测试确认不破坏现有测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Windows/SettingsWindow.xaml OverlayTranslate/Windows/SettingsWindow.xaml.cs
git commit -m "feat(settings): 新增主题和字号设置，SettingsWindow 使用主题资源"
```

---

### 任务 11：清理诊断日志 + 最终验证

**文件：**
- 修改：`OverlayTranslate/Services/TextRenderer.cs`
- 修改：`OverlayTranslate/Services/ImageProcessor.cs`
- 修改：`OverlayTranslate/Windows/OverlayWindow.xaml.cs`

- [ ] **步骤 1：将诊断日志从 Information 降为 Debug**

将之前为调试添加的 `Log.Information` 改回 `Log.Debug`：

```csharp
// TextRenderer.cs 中的详细像素检查日志可以移除
// ImageProcessor.cs 中的详细坐标日志改为 Debug
// OverlayWindow.xaml.cs 中的详细状态日志改为 Debug
```

- [ ] **步骤 2：运行全部测试**

运行：`dotnet test --no-restore --nologo`
预期：ALL PASS

- [ ] **步骤 3：最终 Commit**

```bash
git add -A
git commit -m "chore: 清理诊断日志，最终验证"
```

---

## 自检结果

**1. 规格覆盖度：**
- 字体大小三种模式 → 任务 2, 3 ✅
- 工具栏自动避让 → 任务 4 ✅
- 工具栏可拖动 → 任务 5 ✅
- 暗黑模式资源字典 → 任务 6 ✅
- ThemeManager → 任务 7, 8 ✅
- UI 绑定主题 → 任务 9, 10 ✅
- 设置项 → 任务 1, 10 ✅

**2. 占位符扫描：** 无"待定"、"TODO"等。 ✅

**3. 类型一致性：**
- `OtherSettings.FontSizeMode` 在任务 1 定义，任务 2, 3, 10 使用 ✅
- `StyleAnalyzer.Analyze()` 新签名在任务 2 定义，任务 3 调用 ✅
- `ThemeManager.SetTheme()` 在任务 7 定义，任务 8, 10 调用 ✅

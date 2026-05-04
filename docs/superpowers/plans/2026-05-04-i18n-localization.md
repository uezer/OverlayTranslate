# 多语言本地化实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为 OverlayTranslate 实现中英双语支持，用户可在设置中切换，切换后即时刷新所有 UI 文字。

**架构：** ResourceDictionary + LocExtension 方案。LocManager 静态类管理语言加载和切换，LocExtension 自定义 MarkupExtension 在 XAML 中引用翻译，LocBindingProxy 通过 Binding + PropertyChanged 实现动态刷新。弱引用列表防止代理对象内存泄漏。

**技术栈：** WPF ResourceDictionary、自定义 MarkupExtension、CommunityToolkit.Mvvm

---

## 文件结构

| 操作 | 文件 | 职责 |
|------|------|------|
| 新增 | `Localization/LocManager.cs` | 语言管理核心：加载字典、切换语言、弱引用代理列表 |
| 新增 | `Localization/LocExtension.cs` | XAML MarkupExtension + LocBindingProxy |
| 新增 | `Localization/Strings.zh-CN.xaml` | 中文翻译资源 |
| 新增 | `Localization/Strings.en-US.xaml` | 英文翻译资源 |
| 修改 | `Models/AppSettings.cs:53-58` | OtherSettings 加 Locale 字段 |
| 修改 | `App.xaml.cs:21-65` | OnStartup 中初始化 LocManager，启动错误 MessageBox |
| 修改 | `Windows/SettingsWindow.xaml` | 所有中文 → Loc，加界面语言下拉框 |
| 修改 | `ViewModels/SettingsViewModel.cs` | 加 SelectedLocale，(无) 哨兵值修复 |
| 修改 | `Controls/FloatingToolbar.xaml` | 所有中文 → Loc |
| 修改 | `ViewModels/FloatingToolbarViewModel.cs` | 中文 → LocManager，订阅 Changed |
| 修改 | `Infrastructure/TrayIconManager.cs` | 中文 → LocManager，订阅 Changed |
| 修改 | `Windows/SettingsWindow.xaml.cs:20-24` | MessageBox → LocManager |
| 修改 | `Windows/OverlayWindow.xaml.cs:270` | MessageBox → LocManager |

---

### 任务 1：LocManager + LocExtension 基础设施

**文件：**
- 创建：`OverlayTranslate/Localization/LocManager.cs`
- 创建：`OverlayTranslate/Localization/LocExtension.cs`

- [ ] **步骤 1：创建 LocManager.cs**

```csharp
using System.Globalization;
using System.Windows;

namespace OverlayTranslate.Localization;

public static class LocManager
{
    public static readonly string[] SupportedLocales = ["", "zh-CN", "en-US"];
    public static readonly string[] SupportedLocaleDisplayKeys = ["Lang_Auto", "Lang_zh-CN", "Lang_en-US"];

    private static readonly Dictionary<string, ResourceDictionary> _cache = [];
    private static ResourceDictionary? _currentDict;
    private static readonly List<WeakReference<LocBindingProxy>> _proxies = [];

    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentCulture;

    public static event Action? Changed;

    public static void Initialize(string? configLocale)
    {
        var locale = string.IsNullOrEmpty(configLocale)
            ? DetectSystemLocale()
            : configLocale;
        LoadDictionary(locale);
    }

    public static void SetLocale(string locale)
    {
        if (string.IsNullOrEmpty(locale))
            locale = DetectSystemLocale();
        LoadDictionary(locale);
    }

    public static string this[string key]
    {
        get
        {
            if (_currentDict != null && _currentDict.Contains(key))
                return _currentDict[key] as string ?? key;
            return key;
        }
    }

    internal static void RegisterProxy(LocBindingProxy proxy)
    {
        _proxies.Add(new WeakReference<LocBindingProxy>(proxy));
    }

    internal static void RaiseChangedForProxies()
    {
        for (int i = _proxies.Count - 1; i >= 0; i--)
        {
            if (_proxies[i].TryGetTarget(out var proxy))
                proxy.OnChanged();
            else
                _proxies.RemoveAt(i);
        }
    }

    private static string DetectSystemLocale()
    {
        var uiCulture = CultureInfo.CurrentUICulture.Name;
        return uiCulture.StartsWith("zh") ? "zh-CN" : "en-US";
    }

    private static void LoadDictionary(string locale)
    {
        if (!_cache.TryGetValue(locale, out var dict))
        {
            var uri = new Uri($"/OverlayTranslate;component/Localization/Strings.{locale}.xaml", UriKind.Relative);
            dict = Application.LoadComponent(uri) as ResourceDictionary;
            if (dict != null)
                _cache[locale] = dict;
        }

        if (dict == null) return;

        // 移除旧字典
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_currentDict != null)
            merged.Remove(_currentDict);

        merged.Add(dict);
        _currentDict = dict;
        CurrentCulture = new CultureInfo(locale);

        Changed?.Invoke();
        RaiseChangedForProxies();
    }
}
```

- [ ] **步骤 2：创建 LocExtension.cs**

```csharp
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.ComponentModel;

namespace OverlayTranslate.Localization;

public class LocExtension : MarkupExtension
{
    private readonly string _key;

    public LocExtension(string key) => _key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var proxy = new LocBindingProxy(_key);
        LocManager.RegisterProxy(proxy);

        var binding = new Binding("Value")
        {
            Source = proxy,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

internal class LocBindingProxy : INotifyPropertyChanged
{
    private readonly string _key;
    public string Value => LocManager[_key];

    public LocBindingProxy(string key) => _key = key;

    internal void OnChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过（不报 LocManager/LocExtension 相关错误）

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Localization/LocManager.cs OverlayTranslate/Localization/LocExtension.cs
git commit -m "feat(i18n): 添加 LocManager 和 LocExtension 基础设施"
```

---

### 任务 2：资源字典文件

**文件：**
- 创建：`OverlayTranslate/Localization/Strings.zh-CN.xaml`
- 创建：`OverlayTranslate/Localization/Strings.en-US.xaml`

- [ ] **步骤 1：创建 Strings.zh-CN.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <!-- App -->
    <sys:String x:Key="App_Name">OverlayTranslate</sys:String>
    <sys:String x:Key="App_StartupError">OverlayTranslate 错误</sys:String>
    <sys:String x:Key="App_StartupException">启动异常:</sys:String>

    <!-- 设置窗口 -->
    <sys:String x:Key="Settings_Title">OverlayTranslate 设置</sys:String>
    <sys:String x:Key="Settings_OcrTab">OCR</sys:String>
    <sys:String x:Key="Settings_DefaultOcrEngine">默认 OCR 引擎:</sys:String>
    <sys:String x:Key="Settings_ModelPath">模型路径:</sys:String>
    <sys:String x:Key="Settings_RemoteOcr">远程 OCR</sys:String>
    <sys:String x:Key="Settings_EndpointUrl">端点 URL:</sys:String>
    <sys:String x:Key="Settings_FallbackEngine">回退引擎:</sys:String>
    <sys:String x:Key="Settings_TranslationTab">翻译</sys:String>
    <sys:String x:Key="Settings_DefaultTranslationEngine">默认翻译引擎:</sys:String>
    <sys:String x:Key="Settings_FreeTier">免费版 (api-free.deepl.com)</sys:String>
    <sys:String x:Key="Settings_BaiduTranslation">百度翻译</sys:String>
    <sys:String x:Key="Settings_Secret">密钥:</sys:String>
    <sys:String x:Key="Settings_Model">模型:</sys:String>
    <sys:String x:Key="Settings_MicrosoftTranslation">Microsoft (Bing 翻译)</sys:String>
    <sys:String x:Key="Settings_NoConfigNeeded">无需配置，直接使用</sys:String>
    <sys:String x:Key="Settings_LanguageTab">语言</sys:String>
    <sys:String x:Key="Settings_DefaultSourceLang">默认源语言:</sys:String>
    <sys:String x:Key="Settings_DefaultTargetLang">默认目标语言:</sys:String>
    <sys:String x:Key="Settings_HotkeyTab">热键</sys:String>
    <sys:String x:Key="Settings_ScreenshotHotkey">截图翻译快捷键:</sys:String>
    <sys:String x:Key="Settings_OtherTab">其他</sys:String>
    <sys:String x:Key="Settings_LogLevel">日志级别:</sys:String>
    <sys:String x:Key="Settings_LogFilePath">日志文件路径:</sys:String>
    <sys:String x:Key="Settings_PythonRuntimePath">Python 运行时路径:</sys:String>
    <sys:String x:Key="Settings_PythonHint">留空表示不使用 Python</sys:String>
    <sys:String x:Key="Settings_Theme">主题:</sys:String>
    <sys:String x:Key="Settings_FontSizeMode">字号模式:</sys:String>
    <sys:String x:Key="Settings_CustomFontSize">自定义字号:</sys:String>
    <sys:String x:Key="Settings_Save">保存</sys:String>
    <sys:String x:Key="Settings_Cancel">取消</sys:String>
    <sys:String x:Key="Settings_UiLanguage">界面语言:</sys:String>

    <!-- 工具栏 -->
    <sys:String x:Key="Toolbar_SourceLang">原语言:</sys:String>
    <sys:String x:Key="Toolbar_TargetLang">目标语言:</sys:String>
    <sys:String x:Key="Toolbar_OcrEngine">OCR引擎:</sys:String>
    <sys:String x:Key="Toolbar_TransEngine">翻译引擎:</sys:String>
    <sys:String x:Key="Toolbar_Reselect">重选</sys:String>
    <sys:String x:Key="Toolbar_CopyOriginal">复制原文</sys:String>
    <sys:String x:Key="Toolbar_CopyTranslated">复制译文</sys:String>
    <sys:String x:Key="Toolbar_Exit">退出</sys:String>
    <sys:String x:Key="Toolbar_ShowOriginalImage">显示原图</sys:String>
    <sys:String x:Key="Toolbar_OriginalBgFill">原文底色覆盖</sys:String>
    <sys:String x:Key="Toolbar_TranslatedBgFill">译文底色覆盖</sys:String>
    <sys:String x:Key="Toolbar_OriginalText">原文</sys:String>
    <sys:String x:Key="Toolbar_TranslatedText">译文</sys:String>

    <!-- 托盘菜单 -->
    <sys:String x:Key="Tray_ScreenshotTranslate">截图翻译</sys:String>
    <sys:String x:Key="Tray_Settings">设置</sys:String>
    <sys:String x:Key="Tray_Exit">退出</sys:String>

    <!-- MessageBox -->
    <sys:String x:Key="Msg_SettingsSaved_Title">提示</sys:String>
    <sys:String x:Key="Msg_SettingsSaved_Body">设置已保存。

以下设置立即生效：界面语言、语言、OCR/翻译引擎选择、API Key。
以下设置需要重启应用：热键、日志级别、日志文件路径、Python 路径、OCR 模型路径。</sys:String>
    <sys:String x:Key="Msg_ProcessFailed_Body">处理失败: {0}

请检查引擎配置（右键托盘图标 → 设置）。</sys:String>

    <!-- 通用 -->
    <sys:String x:Key="None">(无)</sys:String>
    <sys:String x:Key="Lang_Auto">跟随系统</sys:String>
    <sys:String x:Key="Lang_zh-CN">中文</sys:String>
    <sys:String x:Key="Lang_en-US">English</sys:String>

    <!-- 引擎显示名 -->
    <sys:String x:Key="Engine_PaddleOCR">PaddleOCR</sys:String>
    <sys:String x:Key="Engine_RemoteOcr">远程 OCR</sys:String>
    <sys:String x:Key="Engine_DeepL">DeepL</sys:String>
    <sys:String x:Key="Engine_Google">Google 翻译</sys:String>
    <sys:String x:Key="Engine_Baidu">百度</sys:String>
    <sys:String x:Key="Engine_OpenAI">OpenAI</sys:String>
    <sys:String x:Key="Engine_Microsoft">Microsoft (Bing 翻译)</sys:String>
</ResourceDictionary>
```

- [ ] **步骤 2：创建 Strings.en-US.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <!-- App -->
    <sys:String x:Key="App_Name">OverlayTranslate</sys:String>
    <sys:String x:Key="App_StartupError">OverlayTranslate Error</sys:String>
    <sys:String x:Key="App_StartupException">Startup error:</sys:String>

    <!-- Settings -->
    <sys:String x:Key="Settings_Title">OverlayTranslate Settings</sys:String>
    <sys:String x:Key="Settings_OcrTab">OCR</sys:String>
    <sys:String x:Key="Settings_DefaultOcrEngine">Default OCR Engine:</sys:String>
    <sys:String x:Key="Settings_ModelPath">Model Path:</sys:String>
    <sys:String x:Key="Settings_RemoteOcr">Remote OCR</sys:String>
    <sys:String x:Key="Settings_EndpointUrl">Endpoint URL:</sys:String>
    <sys:String x:Key="Settings_FallbackEngine">Fallback Engine:</sys:String>
    <sys:String x:Key="Settings_TranslationTab">Translation</sys:String>
    <sys:String x:Key="Settings_DefaultTranslationEngine">Default Translation Engine:</sys:String>
    <sys:String x:Key="Settings_FreeTier">Free tier (api-free.deepl.com)</sys:String>
    <sys:String x:Key="Settings_BaiduTranslation">Baidu Translate</sys:String>
    <sys:String x:Key="Settings_Secret">Secret:</sys:String>
    <sys:String x:Key="Settings_Model">Model:</sys:String>
    <sys:String x:Key="Settings_MicrosoftTranslation">Microsoft (Bing Translator)</sys:String>
    <sys:String x:Key="Settings_NoConfigNeeded">No configuration needed</sys:String>
    <sys:String x:Key="Settings_LanguageTab">Language</sys:String>
    <sys:String x:Key="Settings_DefaultSourceLang">Default Source Language:</sys:String>
    <sys:String x:Key="Settings_DefaultTargetLang">Default Target Language:</sys:String>
    <sys:String x:Key="Settings_HotkeyTab">Hotkey</sys:String>
    <sys:String x:Key="Settings_ScreenshotHotkey">Screenshot Hotkey:</sys:String>
    <sys:String x:Key="Settings_OtherTab">Other</sys:String>
    <sys:String x:Key="Settings_LogLevel">Log Level:</sys:String>
    <sys:String x:Key="Settings_LogFilePath">Log File Path:</sys:String>
    <sys:String x:Key="Settings_PythonRuntimePath">Python Runtime Path:</sys:String>
    <sys:String x:Key="Settings_PythonHint">Leave empty to disable Python</sys:String>
    <sys:String x:Key="Settings_Theme">Theme:</sys:String>
    <sys:String x:Key="Settings_FontSizeMode">Font Size Mode:</sys:String>
    <sys:String x:Key="Settings_CustomFontSize">Custom Font Size:</sys:String>
    <sys:String x:Key="Settings_Save">Save</sys:String>
    <sys:String x:Key="Settings_Cancel">Cancel</sys:String>
    <sys:String x:Key="Settings_UiLanguage">UI Language:</sys:String>

    <!-- Toolbar -->
    <sys:String x:Key="Toolbar_SourceLang">Source:</sys:String>
    <sys:String x:Key="Toolbar_TargetLang">Target:</sys:String>
    <sys:String x:Key="Toolbar_OcrEngine">OCR Engine:</sys:String>
    <sys:String x:Key="Toolbar_TransEngine">Translation:</sys:String>
    <sys:String x:Key="Toolbar_Reselect">Reselect</sys:String>
    <sys:String x:Key="Toolbar_CopyOriginal">Copy Original</sys:String>
    <sys:String x:Key="Toolbar_CopyTranslated">Copy Translated</sys:String>
    <sys:String x:Key="Toolbar_Exit">Exit</sys:String>
    <sys:String x:Key="Toolbar_ShowOriginalImage">Show Original</sys:String>
    <sys:String x:Key="Toolbar_OriginalBgFill">Original BG Fill</sys:String>
    <sys:String x:Key="Toolbar_TranslatedBgFill">Translated BG Fill</sys:String>
    <sys:String x:Key="Toolbar_OriginalText">Original</sys:String>
    <sys:String x:Key="Toolbar_TranslatedText">Translated</sys:String>

    <!-- Tray Menu -->
    <sys:String x:Key="Tray_ScreenshotTranslate">Screenshot Translate</sys:String>
    <sys:String x:Key="Tray_Settings">Settings</sys:String>
    <sys:String x:Key="Tray_Exit">Exit</sys:String>

    <!-- MessageBox -->
    <sys:String x:Key="Msg_SettingsSaved_Title">Notice</sys:String>
    <sys:String x:Key="Msg_SettingsSaved_Body">Settings saved.

These take effect immediately: UI Language, Source/Target Language, OCR/Translation Engine selection, API Key.
These require an app restart: Hotkey, Log Level, Log File Path, Python Path, OCR Model Path.</sys:String>
    <sys:String x:Key="Msg_ProcessFailed_Body">Processing failed: {0}

Please check engine configuration (right-click tray icon → Settings).</sys:String>

    <!-- Common -->
    <sys:String x:Key="None">(None)</sys:String>
    <sys:String x:Key="Lang_Auto">System Default</sys:String>
    <sys:String x:Key="Lang_zh-CN">Chinese</sys:String>
    <sys:String x:Key="Lang_en-US">English</sys:String>

    <!-- Engine Display Names -->
    <sys:String x:Key="Engine_PaddleOCR">PaddleOCR</sys:String>
    <sys:String x:Key="Engine_RemoteOcr">Remote OCR</sys:String>
    <sys:String x:Key="Engine_DeepL">DeepL</sys:String>
    <sys:String x:Key="Engine_Google">Google Translate</sys:String>
    <sys:String x:Key="Engine_Baidu">Baidu</sys:String>
    <sys:String x:Key="Engine_OpenAI">OpenAI</sys:String>
    <sys:String x:Key="Engine_Microsoft">Microsoft (Bing Translator)</sys:String>
</ResourceDictionary>
```

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Localization/Strings.zh-CN.xaml OverlayTranslate/Localization/Strings.en-US.xaml
git commit -m "feat(i18n): 添加中英文资源字典"
```

---

### 任务 3：Config 变更 + LocManager 初始化

**文件：**
- 修改：`OverlayTranslate/Models/AppSettings.cs:53-58`
- 修改：`OverlayTranslate/App.xaml.cs:21-65`

- [ ] **步骤 1：OtherSettings 加 Locale 字段**

在 `OtherSettings` 类末尾（`Theme` 属性之后）添加：

```csharp
public string Locale { get; set; } = "";  // "" = 跟随系统, "zh-CN", "en-US"
```

- [ ] **步骤 2：App.xaml.cs OnStartup 中初始化 LocManager**

在 `ThemeManager.SetTheme(theme);` 之后、显示主窗口之前添加：

```csharp
// 初始化多语言
LocManager.Initialize(configManager.Settings.Other.Locale);
```

并添加 using：
```csharp
using OverlayTranslate.Localization;
```

- [ ] **步骤 3：启动错误 MessageBox 改用 LocManager**

将 `DispatcherUnhandledException` 中的：
```csharp
MessageBox.Show($"启动异常: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
    "OverlayTranslate 错误", MessageBoxButton.OK, MessageBoxImage.Error);
```
改为：
```csharp
MessageBox.Show($"{LocManager["App_StartupException"]} {args.Exception.Message}\n\n{args.Exception.StackTrace}",
    LocManager["App_StartupError"], MessageBoxButton.OK, MessageBoxImage.Error);
```

- [ ] **步骤 4：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Models/AppSettings.cs OverlayTranslate/App.xaml.cs
git commit -m "feat(i18n): 添加 Locale 配置项，初始化 LocManager"
```

---

### 任务 4：本地化 SettingsWindow XAML

**文件：**
- 修改：`OverlayTranslate/Windows/SettingsWindow.xaml`

- [ ] **步骤 1：添加 xmlns:local 命名空间**

在 Window 标签中添加：
```xml
xmlns:local="clr-namespace:OverlayTranslate.Localization"
```

- [ ] **步骤 2：替换所有硬编码中文为 Loc 引用**

逐个替换（按文件中从上到下的顺序）：

```xml
<!-- Title -->
Title="{local:Loc Settings_Title}"

<!-- OCR Tab -->
Header="{local:Loc Settings_OcrTab}"
Text="{local:Loc Settings_DefaultOcrEngine}"
Text="{local:Loc Settings_ModelPath}"
Text="{local:Loc Settings_RemoteOcr}"
Text="{local:Loc Settings_EndpointUrl}"
Text="{local:Loc Settings_FallbackEngine}"

<!-- Translation Tab -->
Header="{local:Loc Settings_TranslationTab}"
Text="{local:Loc Settings_DefaultTranslationEngine}"
Content="{local:Loc Settings_FreeTier}"
Text="{local:Loc Settings_BaiduTranslation}"
Text="{local:Loc Settings_Secret}"
Text="{local:Loc Settings_Model}"
Text="{local:Loc Settings_MicrosoftTranslation}"
Text="{local:Loc Settings_NoConfigNeeded}"

<!-- Language Tab -->
Header="{local:Loc Settings_LanguageTab}"
Text="{local:Loc Settings_DefaultSourceLang}"
Text="{local:Loc Settings_DefaultTargetLang}"

<!-- Hotkey Tab -->
Header="{local:Loc Settings_HotkeyTab}"
Text="{local:Loc Settings_ScreenshotHotkey}"

<!-- Other Tab -->
Header="{local:Loc Settings_OtherTab}"
Text="{local:Loc Settings_LogLevel}"
Text="{local:Loc Settings_LogFilePath}"
Text="{local:Loc Settings_PythonRuntimePath}"
Text="{local:Loc Settings_PythonHint}"
Text="{local:Loc Settings_Theme}"
Text="{local:Loc Settings_FontSizeMode}"
Text="{local:Loc Settings_CustomFontSize}"

<!-- Buttons -->
Content="{local:Loc Settings_Save}"
Content="{local:Loc Settings_Cancel}"
```

- [ ] **步骤 3：在"其他" tab 中添加界面语言下拉框**

在"主题:" ComboBox 之后、"字号模式:" 之前添加：

```xml
<TextBlock Text="{local:Loc Settings_UiLanguage}" FontWeight="SemiBold" Margin="0,0,0,4" />
<ComboBox ItemsSource="{Binding LocaleOptions}" SelectedItem="{Binding SelectedLocale}" Width="200" HorizontalAlignment="Left" Margin="0,0,0,12" />
```

- [ ] **步骤 4：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Windows/SettingsWindow.xaml
git commit -m "feat(i18n): 本地化 SettingsWindow XAML，添加界面语言选择"
```

---

### 任务 5：本地化 SettingsViewModel

**文件：**
- 修改：`OverlayTranslate/ViewModels/SettingsViewModel.cs`

- [ ] **步骤 1：添加 using 和 Locale 相关属性**

```csharp
using OverlayTranslate.Localization;
```

在 `OtherSettings` 区域添加两个新属性：

```csharp
// === 界面语言 ===
[ObservableProperty] private string _selectedLocale = "";

public string[] LocaleOptions { get; } = LocManager.SupportedLocales
    .Select(l => string.IsNullOrEmpty(l) ? LocManager["Lang_Auto"] : LocManager[$"Lang_{l}"])
    .ToArray();

public string[] LocaleValues { get; } = LocManager.SupportedLocales;
```

- [ ] **步骤 2：构造函数中读取 Locale**

在构造函数中 `SelectedTheme` 赋值之后添加：
```csharp
SelectedLocale = LocaleOptions[Array.IndexOf(LocaleValues, configManager.Settings.Other.Locale)];
```

- [ ] **步骤 3：Save() 中写入 Locale**

在 `Settings.Other.Theme = SelectedTheme;` 之后添加：
```csharp
var localeIndex = Array.IndexOf(LocaleOptions, SelectedLocale);
Settings.Other.Locale = localeIndex >= 0 ? LocaleValues[localeIndex] : "";
```

并在 `Save()` 方法中应用语言切换（在 `configManager.Save()` 之后）：
```csharp
LocManager.SetLocale(Settings.Other.Locale);
```

- [ ] **步骤 4：修复 (无) 哨兵值**

将 `private string _selectedOcrFallback = "(无)";` 改为：
```csharp
private string _selectedOcrFallback = "";
```

将 `FallbackOptions` 改为：
```csharp
public string[] FallbackOptions { get; } = ["", "PaddleOCR", "RemoteOCR"];
public string[] FallbackDisplayOptions { get; } = [LocManager["None"], "PaddleOCR", "RemoteOCR"];
```

在 XAML 中用 `DisplayMemberPath` 或将 ItemsSource 绑定到 `FallbackDisplayOptions` 但 SelectedItem 用 `SelectedOcrFallback` 需要注意类型匹配。

**更简单的方案：** 保持 FallbackOptions 为显示值数组，用转换器或直接在 Save/Load 中处理映射。实际上最简单的方案是：

```csharp
// 保持现有 FallbackOptions，但将 "(无)" 改为 LocManager["None"]
public string[] FallbackOptions { get; } = [LocManager["None"], "PaddleOCR", "RemoteOCR"];
```

然后在构造函数中：
```csharp
SelectedOcrFallback = string.IsNullOrEmpty(configManager.Settings.Ocr.FallbackEngine)
    ? LocManager["None"]
    : configManager.Settings.Ocr.FallbackEngine;
```

在 Save() 中：
```csharp
Settings.Ocr.FallbackEngine = SelectedOcrFallback == LocManager["None"] ? null : SelectedOcrFallback;
```

注意：语言切换后 LocManager["None"] 的值会变，所以 Save() 应在 `LocManager.SetLocale()` 之前执行。

- [ ] **步骤 5：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 6：Commit**

```bash
git add OverlayTranslate/ViewModels/SettingsViewModel.cs
git commit -m "feat(i18n): 本地化 SettingsViewModel，添加界面语言选项"
```

---

### 任务 6：本地化 FloatingToolbar XAML

**文件：**
- 修改：`OverlayTranslate/Controls/FloatingToolbar.xaml`

- [ ] **步骤 1：添加 xmlns:loc 命名空间**

在 UserControl 标签中添加：
```xml
xmlns:loc="clr-namespace:OverlayTranslate.Localization"
```

- [ ] **步骤 2：替换所有硬编码中文**

```xml
<!-- Labels -->
Text="{loc:Loc Toolbar_SourceLang}"
Text="{loc:Loc Toolbar_TargetLang}"
Text="{loc:Loc Toolbar_OcrEngine}"
Text="{loc:Loc Toolbar_TransEngine}"

<!-- Buttons -->
Content="{loc:Loc Toolbar_Reselect}"
Content="{loc:Loc Toolbar_CopyOriginal}"
Content="{loc:Loc Toolbar_CopyTranslated}"
Content="{loc:Loc Toolbar_Exit}"
Content="{loc:Loc Toolbar_ShowOriginalImage}"

<!-- Checkboxes -->
Content="{loc:Loc Toolbar_OriginalBgFill}"
Content="{loc:Loc Toolbar_TranslatedBgFill}"
```

注意：`ViewModeText` 已经是从 ViewModel 绑定的 `{Binding ViewModeText}`，不需要改为 Loc——由 FloatingToolbarViewModel 处理。

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Controls/FloatingToolbar.xaml
git commit -m "feat(i18n): 本地化 FloatingToolbar XAML"
```

---

### 任务 7：本地化 FloatingToolbarViewModel

**文件：**
- 修改：`OverlayTranslate/ViewModels/FloatingToolbarViewModel.cs`

- [ ] **步骤 1：添加 using 和替换硬编码中文**

```csharp
using OverlayTranslate.Localization;
```

将所有 `"原文"` 替换为 `LocManager["Toolbar_OriginalText"]`，所有 `"译文"` 替换为 `LocManager["Toolbar_TranslatedText"]`。

构造函数中：
```csharp
_viewModeText = LocManager["Toolbar_OriginalText"];
```

ToggleViewMode() 中：
```csharp
ViewModeText = _currentViewMode switch
{
    OverlayViewMode.OriginalImage => LocManager["Toolbar_OriginalText"],
    OverlayViewMode.OriginalText => LocManager["Toolbar_TranslatedText"],
    _ => LocManager["Toolbar_OriginalText"]
};
```

SetViewMode() 中：
```csharp
ViewModeText = mode switch
{
    OverlayViewMode.OriginalImage => LocManager["Toolbar_OriginalText"],
    OverlayViewMode.OriginalText => LocManager["Toolbar_TranslatedText"],
    _ => LocManager["Toolbar_OriginalText"]
};
```

ShowOriginalImage() 中：
```csharp
ViewModeText = LocManager["Toolbar_OriginalText"];
```

- [ ] **步骤 2：订阅 LocManager.Changed 刷新动态文字**

在构造函数末尾添加：
```csharp
LocManager.Changed += RefreshLocalizedStrings;
```

添加方法：
```csharp
private void RefreshLocalizedStrings()
{
    // 重新计算 ViewModeText
    ViewModeText = _currentViewMode switch
    {
        OverlayViewMode.OriginalImage => LocManager["Toolbar_OriginalText"],
        OverlayViewMode.OriginalText => LocManager["Toolbar_TranslatedText"],
        _ => LocManager["Toolbar_OriginalText"]
    };
}
```

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/ViewModels/FloatingToolbarViewModel.cs
git commit -m "feat(i18n): 本地化 FloatingToolbarViewModel 动态文字"
```

---

### 任务 8：本地化 TrayIconManager

**文件：**
- 修改：`OverlayTranslate/Infrastructure/TrayIconManager.cs`

- [ ] **步骤 1：添加 using 并替换硬编码中文**

```csharp
using OverlayTranslate.Localization;
```

将菜单项 Header 替换：
```csharp
var screenshotItem = new MenuItem { Header = LocManager["Tray_ScreenshotTranslate"] };
// ...
var settingsItem = new MenuItem { Header = LocManager["Tray_Settings"] };
// ...
var exitItem = new MenuItem { Header = LocManager["Tray_Exit"] };
```

- [ ] **步骤 2：订阅 LocManager.Changed 刷新菜单文字**

```csharp
LocManager.Changed += () =>
{
    screenshotItem.Header = LocManager["Tray_ScreenshotTranslate"];
    settingsItem.Header = LocManager["Tray_Settings"];
    exitItem.Header = LocManager["Tray_Exit"];
};
```

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Infrastructure/TrayIconManager.cs
git commit -m "feat(i18n): 本地化托盘菜单"
```

---

### 任务 9：本地化 MessageBox 弹窗

**文件：**
- 修改：`OverlayTranslate/Windows/SettingsWindow.xaml.cs:20-24`
- 修改：`OverlayTranslate/Windows/OverlayWindow.xaml.cs:270-271`

- [ ] **步骤 1：SettingsWindow.xaml.cs — 添加 using 并替换 MessageBox**

```csharp
using OverlayTranslate.Localization;
```

将 `OnSaveClick` 中的 MessageBox 改为：
```csharp
MessageBox.Show(LocManager["Msg_SettingsSaved_Body"],
    LocManager["Msg_SettingsSaved_Title"], MessageBoxButton.OK, MessageBoxImage.Information);
```

- [ ] **步骤 2：OverlayWindow.xaml.cs — 添加 using 并替换 MessageBox**

```csharp
using OverlayTranslate.Localization;
```

将 `ProcessSelectionAsync` catch 块中的 MessageBox 改为：
```csharp
MessageBox.Show(string.Format(LocManager["Msg_ProcessFailed_Body"], ex.Message),
    LocManager["App_Name"], MessageBoxButton.OK, MessageBoxImage.Warning);
```

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Windows/SettingsWindow.xaml.cs OverlayTranslate/Windows/OverlayWindow.xaml.cs
git commit -m "feat(i18n): 本地化 MessageBox 弹窗"
```

---

### 任务 10：引擎显示名本地化

**文件：**
- 修改：`OverlayTranslate/App.xaml.cs`
- 修改：`OverlayTranslate/Controls/FloatingToolbar.xaml.cs`（如果引擎 ComboBox 有自定义 ItemTemplate）

- [ ] **步骤 1：在 App.xaml.cs 中添加引擎 key → 本地化 key 的映射**

在引擎字典注册之后，添加一个静态映射：

```csharp
// 引擎显示名映射（注册为 Singleton 供其他组件使用）
services.AddSingleton<EngineDisplayMap>();
```

创建新文件 `OverlayTranslate/Localization/EngineDisplayMap.cs`：

```csharp
namespace OverlayTranslate.Localization;

public class EngineDisplayMap
{
    private readonly Dictionary<string, string> _map = new()
    {
        ["PaddleOCR"] = "Engine_PaddleOCR",
        ["RemoteOCR"] = "Engine_RemoteOcr",
        ["DeepL"] = "Engine_DeepL",
        ["Google"] = "Engine_Google",
        ["百度"] = "Engine_Baidu",
        ["OpenAI"] = "Engine_OpenAI",
        ["Microsoft"] = "Engine_Microsoft"
    };

    public string GetDisplayName(string engineKey)
    {
        if (_map.TryGetValue(engineKey, out var locKey))
            return LocManager[locKey];
        return engineKey;
    }

    public string[] GetLocalizedNames(string[] engineKeys)
    {
        return engineKeys.Select(GetDisplayName).ToArray();
    }
}
```

- [ ] **步骤 2：FloatingToolbar 中引擎 ComboBox 使用本地化显示名**

检查 FloatingToolbar.xaml 中引擎 ComboBox 是否有 ItemTemplate。如果有，用 DisplayMemberPath 替代或在 ItemTemplate 中用 LocExtension。如果没有（直接显示 key），需要在 FloatingToolbarViewModel 中添加本地化逻辑。

实际上，FloatingToolbar 中引擎 ComboBox 的 ItemsSource 是通过 `SetEngines()` 方法从外部设置的。当前直接传入引擎 key 数组（如 `["PaddleOCR", "RemoteOCR"]`）。

方案：在 `SetEngines` 中传入显示名数组，但保留 key 数组用于查找。

在 FloatingToolbarViewModel 中添加：
```csharp
public string[] OcrEngineDisplayNames { get; private set; } = [];
public string[] TranslationEngineDisplayNames { get; private set; } = [];

public void SetEngines(string[] ocrNames, string[] transNames, EngineDisplayMap displayMap)
{
    OcrEngineNames = ocrNames;
    TranslationEngineNames = transNames;
    OcrEngineDisplayNames = displayMap.GetLocalizedNames(ocrNames);
    TranslationEngineDisplayNames = displayMap.GetLocalizedNames(transNames);
    OnPropertyChanged(nameof(OcrEngineDisplayNames));
    OnPropertyChanged(nameof(TranslationEngineDisplayNames));
}
```

FloatingToolbar.xaml 中 ComboBox 的 ItemsSource 改为绑定 `{Binding OcrEngineDisplayNames}`，但 SelectedItem 需要通过索引或转换器映射回原始 key。

**更简单的方案：** 不修改 ComboBox 绑定，而是在传入引擎列表时就传入本地化后的显示名。让 `GetAvailableOcrEngines()` 返回显示名而非 key。但这样会破坏内部 key 查找逻辑。

**推荐方案：** 在 FloatingToolbar 中使用两个平行数组——keys 和 display names。ComboBox 绑定到 display names，通过 SelectedIndex 同步 key。

- [ ] **步骤 3：编译验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：通过

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Localization/EngineDisplayMap.cs OverlayTranslate/App.xaml.cs
git commit -m "feat(i18n): 引擎显示名本地化"
```

---

### 任务 11：整体验证

- [ ] **步骤 1：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：0 个错误

- [ ] **步骤 2：运行测试**

运行：`dotnet test`
预期：所有测试通过

- [ ] **步骤 3：Commit 最终调整（如有）**

```bash
git add -A
git commit -m "fix(i18n): 修复编译问题和最终调整"
```

---

## 自检

**1. 规格覆盖度：**
- LocManager + LocExtension → 任务 1
- 资源字典文件 → 任务 2
- AppSettings Locale 字段 → 任务 3
- LocManager 初始化 → 任务 3
- SettingsWindow 本地化 → 任务 4
- SettingsViewModel Locale 选项 → 任务 5
- FloatingToolbar XAML 本地化 → 任务 6
- FloatingToolbarViewModel 本地化 → 任务 7
- TrayIconManager 本地化 → 任务 8
- MessageBox 本地化 → 任务 9
- 引擎显示名本地化 → 任务 10
- 整体验证 → 任务 11

**2. 占位符扫描：** 无 "待定"、"TODO" 等占位符。

**3. 类型一致性：** 所有任务中 LocManager、LocExtension、LocBindingProxy、EngineDisplayMap 的 API 签名一致。`LocManager[key]` 索引器在所有使用处保持一致。

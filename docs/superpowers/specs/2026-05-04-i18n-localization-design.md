# 多语言（i18n）本地化设计规格

## 目标

为 OverlayTranslate 实现多语言支持，默认支持中文和英文，用户可在设置中切换，切换后即时刷新所有 UI 文字（无需重启）。

## 技术方案

采用 **ResourceDictionary + LocExtension** 方案：
- 每种语言一个 XAML ResourceDictionary 文件
- 自定义 `LocExtension` MarkupExtension 在 XAML 中引用翻译
- `LocManager` 静态类管理语言加载和切换
- 启动时跟随系统语言，用户可在设置中手动切换

## 技术栈

- WPF ResourceDictionary（XAML 资源字典）
- 自定义 MarkupExtension
- CommunityToolkit.Mvvm（ViewModel 绑定）
- Microsoft.Extensions.DependencyInjection（保持不变）

---

## 新增文件

### `Localization/LocManager.cs`

静态类，管理语言切换的核心。

```csharp
public static class LocManager
{
    // 支持的语言
    public static readonly string[] SupportedLocales = ["zh-CN", "en-US"];

    // 当前语言
    public static CultureInfo CurrentCulture { get; private set; }

    // 语言切换事件
    public static event Action? Changed;

    // 初始化：从 Config 读取 Locale，为空则检测系统语言
    public static void Initialize(string? configLocale);

    // 切换语言：替换 ResourceDictionary，触发 Changed
    public static void SetLocale(string locale);

    // 索引器：供 C# 代码获取翻译
    public static string this[string key] { get; }

    // 内部：加载语言 ResourceDictionary
    private static void LoadDictionary(string locale);
}
```

切换逻辑：
1. 从 `Application.Current.Resources.MergedDictionaries` 中移除旧的语言字典
2. 加载新的 `Strings.{locale}.xaml` 并添加到 MergedDictionaries
3. 触发 `Changed` 事件

### `Localization/LocExtension.cs`

自定义 MarkupExtension，让 XAML 可以用 `{local:Loc Key}` 引用翻译。

```csharp
public class LocExtension : MarkupExtension
{
    private readonly string _key;

    public LocExtension(string key) => _key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 返回一个 Binding，Path 指向 LocManager 的代理属性
        // 这样语言切换时自动刷新，无需手动 UpdateTarget
        var binding = new Binding("Value")
        {
            Source = new LocBindingProxy(_key),
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

// 代理对象：监听 LocManager.Changed，触发 PropertyChanged 刷新绑定
// 使用 WeakEventManager 防止内存泄漏（窗口关闭时自动 GC）
internal class LocBindingProxy : INotifyPropertyChanged
{
    private readonly string _key;
    public string Value => LocManager[_key];

    public LocBindingProxy(string key)
    {
        _key = key;
        LocManager.Changed += OnChanged;
    }

    private void OnChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    // WeakEventManager 不适用于 Action 事件，
    // 改用 LocManager 内部持有 WeakReference<LocBindingProxy> 列表，
    // 切换语言时遍历弱引用列表，跳过已 GC 的代理。
    public event PropertyChangedEventHandler? PropertyChanged;
}

// LocManager 内部维护：
private static readonly List<WeakReference<LocBindingProxy>> _proxies = [];

// LocBindingProxy 构造时注册到 _proxies
// LocManager.SetLocale() 中遍历 _proxies，对存活的 proxy 调用 OnChanged
```

### `Localization/Strings.zh-CN.xaml`

中文资源字典，约 45 个 key。所有 key 列表见下方"翻译 key 总览"章节。

### `Localization/Strings.en-US.xaml`

英文资源字典，与中文一一对应。

---

## 翻译 key 总览

### 设置窗口（SettingsWindow.xaml）— 27 个 key

| Key | 中文 | English |
|-----|------|---------|
| `Settings_Title` | OverlayTranslate 设置 | OverlayTranslate Settings |
| `Settings_OcrTab` | OCR | OCR |
| `Settings_DefaultOcrEngine` | 默认 OCR 引擎: | Default OCR Engine: |
| `Settings_ModelPath` | 模型路径: | Model Path: |
| `Settings_RemoteOcr` | 远程 OCR | Remote OCR |
| `Settings_EndpointUrl` | 端点 URL: | Endpoint URL: |
| `Settings_FallbackEngine` | 回退引擎: | Fallback Engine: |
| `Settings_TranslationTab` | 翻译 | Translation |
| `Settings_DefaultTranslationEngine` | 默认翻译引擎: | Default Translation Engine: |
| `Settings_FreeTier` | 免费版 (api-free.deepl.com) | Free tier (api-free.deepl.com) |
| `Settings_BaiduTranslation` | 百度翻译 | Baidu Translate |
| `Settings_Secret` | 密钥: | Secret: |
| `Settings_Model` | 模型: | Model: |
| `Settings_MicrosoftTranslation` | Microsoft (Bing 翻译) | Microsoft (Bing Translator) |
| `Settings_NoConfigNeeded` | 无需配置，直接使用 | No configuration needed |
| `Settings_LanguageTab` | 语言 | Language |
| `Settings_DefaultSourceLang` | 默认源语言: | Default Source Language: |
| `Settings_DefaultTargetLang` | 默认目标语言: | Default Target Language: |
| `Settings_HotkeyTab` | 热键 | Hotkey |
| `Settings_ScreenshotHotkey` | 截图翻译快捷键: | Screenshot Hotkey: |
| `Settings_OtherTab` | 其他 | Other |
| `Settings_LogLevel` | 日志级别: | Log Level: |
| `Settings_LogFilePath` | 日志文件路径: | Log File Path: |
| `Settings_PythonRuntimePath` | Python 运行时路径: | Python Runtime Path: |
| `Settings_PythonHint` | 留空表示不使用 Python | Leave empty to disable Python |
| `Settings_Theme` | 主题: | Theme: |
| `Settings_FontSizeMode` | 字号模式: | Font Size Mode: |
| `Settings_CustomFontSize` | 自定义字号: | Custom Font Size: |
| `Settings_Save` | 保存 | Save |
| `Settings_Cancel` | 取消 | Cancel |
| `Settings_UiLanguage` | 界面语言: | UI Language: |

### 工具栏（FloatingToolbar.xaml）— 11 个 key

| Key | 中文 | English |
|-----|------|---------|
| `Toolbar_SourceLang` | 原语言: | Source: |
| `Toolbar_TargetLang` | 目标语言: | Target: |
| `Toolbar_OcrEngine` | OCR引擎: | OCR Engine: |
| `Toolbar_TransEngine` | 翻译引擎: | Translation: |
| `Toolbar_Reselect` | 重选 | Reselect |
| `Toolbar_CopyOriginal` | 复制原文 | Copy Original |
| `Toolbar_CopyTranslated` | 复制译文 | Copy Translated |
| `Toolbar_Exit` | 退出 | Exit |
| `Toolbar_ShowOriginalImage` | 显示原图 | Show Original |
| `Toolbar_OriginalBgFill` | 原文底色覆盖 | Original BG Fill |
| `Toolbar_TranslatedBgFill` | 译文底色覆盖 | Translated BG Fill |

### ViewModel 动态文字 — 2 个 key

| Key | 中文 | English |
|-----|------|---------|
| `Toolbar_OriginalText` | 原文 | Original |
| `Toolbar_TranslatedText` | 译文 | Translated |

### 托盘菜单（TrayIconManager.cs）— 3 个 key

| Key | 中文 | English |
|-----|------|---------|
| `Tray_ScreenshotTranslate` | 截图翻译 | Screenshot Translate |
| `Tray_Settings` | 设置 | Settings |
| `Tray_Exit` | 退出 | Exit |

### MessageBox 弹窗 — 7 个 key

| Key | 中文 | English |
|-----|------|---------|
| `App_Name` | OverlayTranslate | OverlayTranslate |
| `App_StartupError` | OverlayTranslate 错误 | OverlayTranslate Error |
| `App_StartupException` | 启动异常: | Startup error: |
| `Msg_SettingsSaved_Title` | 提示 | Notice |
| `Msg_SettingsSaved_Body` | 设置已保存。… | Settings saved. … |
| `Msg_ProcessFailed_Body` | 处理失败: …\n\n请检查引擎配置… | Processing failed: …\n\nPlease check engine settings… |

### 设置 ViewModel — 1 个 key

| Key | 中文 | English |
|-----|------|---------|
| `None` | (无) | (None) |

### 界面语言下拉框 — 2 个 key

| Key | 中文 | English |
|-----|------|---------|
| `Lang_zh-CN` | 中文 | Chinese |
| `Lang_en-US` | English | English |

### 引擎显示名 — 约 8 个 key

| Key | 中文 | English |
|-----|------|---------|
| `Engine_PaddleOCR` | PaddleOCR | PaddleOCR |
| `Engine_RemoteOcr` | 远程 OCR | Remote OCR |
| `Engine_DeepL` | DeepL | DeepL |
| `Engine_Baidu` | 百度 | Baidu |
| `Engine_OpenAI` | OpenAI | OpenAI |
| `Engine_Microsoft` | Microsoft (Bing 翻译) | Microsoft (Bing Translator) |
| `Engine_Google` | Google 翻译 | Google Translate |

---

## 修改文件

### `Models/AppSettings.cs`

OtherSettings 增加 `Locale` 字段：
```csharp
public class OtherSettings
{
    // ... 现有字段 ...
    public string Locale { get; set; } = "";  // "" = 跟随系统, "zh-CN", "en-US"
}
```

### `Windows/SettingsWindow.xaml`

- 所有硬编码中文字符串替换为 `{local:Loc Key}`
- "其他" tab 中"主题"下方新增"界面语言:" ComboBox
- ComboBox ItemsSource 绑定到支持的语言列表，SelectedValue 绑定到 `SelectedLocale`

### `ViewModels/SettingsViewModel.cs`

- 新增 `[ObservableProperty] private string _selectedLocale = "";`
- 构造函数中读取 `configManager.Settings.Other.Locale`，为空时显示"跟随系统"
- Save() 中写入 Locale 字段
- `(无)` 哨兵值改为 `null`/空字符串，显示文本通过 `LocManager["None"]` 获取

### `Controls/FloatingToolbar.xaml`

所有硬编码中文字符串替换为 `{local:Loc Key}`

### `ViewModels/FloatingToolbarViewModel.cs`

所有硬编码中文字符串（`"原文"`、`"译文"`）替换为 `LocManager["Toolbar_OriginalText"]` 等
订阅 `LocManager.Changed` 事件，在语言切换时刷新 `ViewModeText`

### `Infrastructure/TrayIconManager.cs`

菜单项 Header 替换为 `LocManager["Tray_ScreenshotTranslate"]` 等
订阅 `LocManager.Changed` 刷新菜单文字

### `App.xaml.cs`

- 在 `OnStartup` 中调用 `LocManager.Initialize(configManager.Settings.Other.Locale)`
- 在 DI 注册时添加语言字典到 MergedDictionaries
- 启动错误 MessageBox 使用 LocManager

### `Windows/SettingsWindow.xaml.cs`

设置保存成功 MessageBox 使用 LocManager

### `Windows/OverlayWindow.xaml.cs`

处理失败 MessageBox 使用 LocManager

### `App.xaml.cs` 引擎注册

引擎字典 key 保持不变（"百度"、"PaddleOCR"），但增加显示名映射：
```csharp
// 引擎 key → 本地化 key 的映射
var engineDisplayMap = new Dictionary<string, string>
{
    ["PaddleOCR"] = "Engine_PaddleOCR",
    ["百度"] = "Engine_Baidu",
    ["DeepL"] = "Engine_DeepL",
    // ...
};
```

下拉框显示 `LocManager[engineDisplayMap[key]]`，选中值仍是原始 key。

---

## 文件变更总览

| 操作 | 文件 |
|------|------|
| 新增 | `Localization/LocManager.cs` |
| 新增 | `Localization/LocExtension.cs` |
| 新增 | `Localization/Strings.zh-CN.xaml` |
| 新增 | `Localization/Strings.en-US.xaml` |
| 修改 | `Models/AppSettings.cs`（OtherSettings 加 Locale） |
| 修改 | `Windows/SettingsWindow.xaml`（中文 → Loc，加界面语言下拉框） |
| 修改 | `ViewModels/SettingsViewModel.cs`（加 SelectedLocale，(无) 修复） |
| 修改 | `Controls/FloatingToolbar.xaml`（中文 → Loc） |
| 修改 | `ViewModels/FloatingToolbarViewModel.cs`（中文 → LocManager） |
| 修改 | `Infrastructure/TrayIconManager.cs`（中文 → LocManager） |
| 修改 | `App.xaml.cs`（初始化 LocManager，引擎显示名映射） |
| 修改 | `Windows/SettingsWindow.xaml.cs`（MessageBox → LocManager） |
| 修改 | `Windows/OverlayWindow.xaml.cs`（MessageBox → LocManager） |

## 验证标准

1. 构建通过，无编译错误
2. 系统语言为中文时，默认显示中文界面
3. 系统语言为非中文时，默认显示英文界面
4. 设置中切换语言后，所有已打开窗口的文字立即刷新（无需重启）
5. 托盘菜单、工具栏、设置窗口、MessageBox 的文字都正确本地化
6. 引擎下拉框显示本地化名称，但引擎切换和翻译功能正常
7. 切换语言后重启应用，语言设置保持
8. `(无)` 选项在两种语言下都能正常工作（显示和逻辑）

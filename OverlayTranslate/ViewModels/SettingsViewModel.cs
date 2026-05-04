using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OverlayTranslate.Infrastructure;
using Serilog;

namespace OverlayTranslate.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;

    // OCR
    [ObservableProperty] private string _selectedOcrEngine = "PaddleOCR";
    [ObservableProperty] private string[] _ocrEngineNames = [];
    [ObservableProperty] private string[] _fallbackOptions = [];

    [ObservableProperty] private string _paddleModelPath = "inference/";
    [ObservableProperty] private string _remoteOcrEndpoint = "";
    [ObservableProperty] private string _remoteOcrApiKey = "";
    [ObservableProperty] private string _selectedOcrFallback = "(无)";

    // 翻译
    [ObservableProperty] private string _selectedTranslationEngine = "Google";
    [ObservableProperty] private string[] _translationEngineNames = [];

    [ObservableProperty] private string _deepLApiKey = "";
    [ObservableProperty] private bool _deepLFreeTier = true;
    [ObservableProperty] private string _baiduAppId = "";
    [ObservableProperty] private string _baiduSecret = "";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _openAiModel = "gpt-4o-mini";

    // 语言
    [ObservableProperty] private string[] _languages = ["auto", "zh", "zh-CN", "en", "ja", "ko", "fr", "de", "es", "ru"];
    [ObservableProperty] private string _sourceLanguage = "auto";
    [ObservableProperty] private string _targetLanguage = "zh-CN";

    // 热键
    [ObservableProperty] private bool _hotkeyCtrl = true;
    [ObservableProperty] private bool _hotkeyAlt = false;
    [ObservableProperty] private bool _hotkeyShift = true;
    [ObservableProperty] private string _hotkeyKey = "T";

    // 其他
    [ObservableProperty] private string[] _logLevels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
    [ObservableProperty] private string _selectedLogLevel = "Information";
    [ObservableProperty] private string _logFile = "logs/app.log";
    [ObservableProperty] private string _pythonRuntimePath = "";
    [ObservableProperty] private string[] _themes = ["system", "light", "dark"];
    [ObservableProperty] private string _selectedTheme = "system";
    [ObservableProperty] private string[] _fontModes = ["auto", "fit-width", "custom"];
    [ObservableProperty] private string _selectedFontMode = "auto";
    [ObservableProperty] private int _customFontSize = 14;

    public SettingsViewModel(
        ConfigManager configManager,
        Dictionary<string, Engines.IOcrEngine> ocrEngines,
        Dictionary<string, Engines.ITranslationEngine> translationEngines)
    {
        _configManager = configManager;
        _ocrEngineNames = ocrEngines.Keys.ToArray();
        _translationEngineNames = translationEngines.Keys.ToArray();
        _fallbackOptions = new[] { "(无)" }.Concat(_ocrEngineNames).ToArray();

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _configManager.Settings;

        // OCR
        SelectedOcrEngine = s.Ocr.ActiveEngine;
        PaddleModelPath = s.Ocr.Engines.GetValueOrDefault("PaddleOCR")?.GetValueOrDefault("modelPath") ?? "inference/";
        RemoteOcrEndpoint = s.Ocr.Engines.GetValueOrDefault("RemoteOCR")?.GetValueOrDefault("endpoint") ?? "";
        RemoteOcrApiKey = s.Ocr.Engines.GetValueOrDefault("RemoteOCR")?.GetValueOrDefault("apiKey") ?? "";
        SelectedOcrFallback = string.IsNullOrEmpty(s.Ocr.FallbackEngine) ? "(无)" : s.Ocr.FallbackEngine;

        // 翻译
        SelectedTranslationEngine = s.Translation.ActiveEngine;
        DeepLApiKey = s.Translation.Engines.GetValueOrDefault("DeepL")?.GetValueOrDefault("apiKey") ?? "";
        DeepLFreeTier = s.Translation.Engines.GetValueOrDefault("DeepL")?.GetValueOrDefault("freeTier") != "false";
        BaiduAppId = s.Translation.Engines.GetValueOrDefault("Baidu")?.GetValueOrDefault("appId") ?? "";
        BaiduSecret = s.Translation.Engines.GetValueOrDefault("Baidu")?.GetValueOrDefault("secret") ?? "";
        OpenAiApiKey = s.Translation.Engines.GetValueOrDefault("OpenAI")?.GetValueOrDefault("apiKey") ?? "";
        OpenAiModel = s.Translation.Engines.GetValueOrDefault("OpenAI")?.GetValueOrDefault("model") ?? "gpt-4o-mini";

        // 语言
        SourceLanguage = s.Language.Source;
        TargetLanguage = s.Language.Target;

        // 热键
        HotkeyCtrl = s.Hotkey.Modifiers.Contains("Ctrl");
        HotkeyAlt = s.Hotkey.Modifiers.Contains("Alt");
        HotkeyShift = s.Hotkey.Modifiers.Contains("Shift");
        HotkeyKey = s.Hotkey.Key;

        // 其他
        SelectedLogLevel = s.Logging.Level;
        LogFile = s.Logging.File;
        PythonRuntimePath = s.Python.RuntimePath;
        SelectedTheme = s.Other.Theme;
        SelectedFontMode = s.Other.FontSizeMode;
        CustomFontSize = s.Other.CustomFontSize;
    }

    [RelayCommand]
    public void Save()
    {
        var s = _configManager.Settings;

        // OCR
        s.Ocr.ActiveEngine = SelectedOcrEngine;
        s.Ocr.Engines["PaddleOCR"] = new Dictionary<string, string> { ["modelPath"] = PaddleModelPath };
        s.Ocr.Engines["RemoteOCR"] = new Dictionary<string, string>
        {
            ["endpoint"] = RemoteOcrEndpoint,
            ["apiKey"] = RemoteOcrApiKey
        };
        s.Ocr.FallbackEngine = SelectedOcrFallback == "(无)" ? null : SelectedOcrFallback;

        // 翻译
        s.Translation.ActiveEngine = SelectedTranslationEngine;
        s.Translation.Engines["DeepL"] = new Dictionary<string, string>
        {
            ["apiKey"] = DeepLApiKey,
            ["freeTier"] = DeepLFreeTier ? "true" : "false"
        };
        s.Translation.Engines["Baidu"] = new Dictionary<string, string>
        {
            ["appId"] = BaiduAppId,
            ["secret"] = BaiduSecret
        };
        s.Translation.Engines["OpenAI"] = new Dictionary<string, string>
        {
            ["apiKey"] = OpenAiApiKey,
            ["model"] = OpenAiModel
        };

        // 语言
        s.Language.Source = SourceLanguage;
        s.Language.Target = TargetLanguage;

        // 热键
        var modifiers = new List<string>();
        if (HotkeyCtrl) modifiers.Add("Ctrl");
        if (HotkeyAlt) modifiers.Add("Alt");
        if (HotkeyShift) modifiers.Add("Shift");
        s.Hotkey.Modifiers = modifiers.ToArray();
        s.Hotkey.Key = HotkeyKey;

        // 其他
        s.Logging.Level = SelectedLogLevel;
        s.Logging.File = LogFile;
        s.Python.RuntimePath = PythonRuntimePath;
        s.Other.Theme = SelectedTheme;
        s.Other.FontSizeMode = SelectedFontMode;
        s.Other.CustomFontSize = CustomFontSize;

        Infrastructure.ThemeManager.SetTheme(SelectedTheme);

        Log.Information("设置保存: OCR={Ocr}, Translation={Trans}, Source={Src}, Target={Tgt}",
            s.Ocr.ActiveEngine, s.Translation.ActiveEngine, s.Language.Source, s.Language.Target);
        _configManager.Save();
    }
}

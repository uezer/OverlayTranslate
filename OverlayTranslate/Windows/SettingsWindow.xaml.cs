using System.Windows;
using OverlayTranslate.Engines;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Models;
using Serilog;

namespace OverlayTranslate.Windows;

public partial class SettingsWindow : Window
{
    private readonly ConfigManager _configManager;

    public SettingsWindow(
        ConfigManager configManager,
        Dictionary<string, IOcrEngine> ocrEngines,
        Dictionary<string, ITranslationEngine> translationEngines)
    {
        _configManager = configManager;
        _ocrEngineNames = ocrEngines.Keys.ToArray();
        _translationEngineNames = translationEngines.Keys.ToArray();
        InitializeComponent();
        LoadSettings();
    }

    private readonly string[] _ocrEngineNames;
    private readonly string[] _translationEngineNames;

    private void LoadSettings()
    {
        var settings = _configManager.Settings;

        // OCR
        foreach (var e in _ocrEngineNames) OcrEngineComboBox.Items.Add(e);
        OcrEngineComboBox.SelectedItem = settings.Ocr.ActiveEngine;

        PaddleModelPath.Text = settings.Ocr.Engines
            .GetValueOrDefault("PaddleOCR")?.GetValueOrDefault("modelPath") ?? "inference/";

        RemoteOcrEndpoint.Text = settings.Ocr.Engines
            .GetValueOrDefault("RemoteOCR")?.GetValueOrDefault("endpoint") ?? "";
        RemoteOcrApiKey.Text = settings.Ocr.Engines
            .GetValueOrDefault("RemoteOCR")?.GetValueOrDefault("apiKey") ?? "";

        var fallbackItems = new[] { "(无)" }.Concat(_ocrEngineNames).ToArray();
        foreach (var f in fallbackItems) OcrFallbackComboBox.Items.Add(f);
        OcrFallbackComboBox.SelectedItem = string.IsNullOrEmpty(settings.Ocr.FallbackEngine)
            ? "(无)" : settings.Ocr.FallbackEngine;

        // 翻译
        foreach (var e in _translationEngineNames) TranslationEngineComboBox.Items.Add(e);
        TranslationEngineComboBox.SelectedItem = settings.Translation.ActiveEngine;

        DeepLApiKey.Text = settings.Translation.Engines
            .GetValueOrDefault("DeepL")?.GetValueOrDefault("apiKey") ?? "";
        DeepLFreeTier.IsChecked = settings.Translation.Engines
            .GetValueOrDefault("DeepL")?.GetValueOrDefault("freeTier") != "false";

        BaiduAppId.Text = settings.Translation.Engines
            .GetValueOrDefault("Baidu")?.GetValueOrDefault("appId") ?? "";
        BaiduSecret.Text = settings.Translation.Engines
            .GetValueOrDefault("Baidu")?.GetValueOrDefault("secret") ?? "";

        OpenAIApiKey.Text = settings.Translation.Engines
            .GetValueOrDefault("OpenAI")?.GetValueOrDefault("apiKey") ?? "";
        OpenAIModel.Text = settings.Translation.Engines
            .GetValueOrDefault("OpenAI")?.GetValueOrDefault("model") ?? "gpt-4o-mini";

        MicrosoftApiKey.Text = settings.Translation.Engines
            .GetValueOrDefault("Microsoft")?.GetValueOrDefault("apiKey") ?? "";
        MicrosoftRegion.Text = settings.Translation.Engines
            .GetValueOrDefault("Microsoft")?.GetValueOrDefault("region") ?? "";
        MicrosoftEndpoint.Text = settings.Translation.Engines
            .GetValueOrDefault("Microsoft")?.GetValueOrDefault("endpoint") ?? "";

        // 语言
        var languages = new[] { "auto", "zh", "zh-CN", "en", "ja", "ko", "fr", "de", "es", "ru" };
        foreach (var l in languages)
        {
            SourceLanguageComboBox.Items.Add(l);
            TargetLanguageComboBox.Items.Add(l);
        }
        SourceLanguageComboBox.SelectedItem = settings.Language.Source;
        TargetLanguageComboBox.SelectedItem = settings.Language.Target;

        // 热键
        HotkeyCtrl.IsChecked = settings.Hotkey.Modifiers.Contains("Ctrl");
        HotkeyAlt.IsChecked = settings.Hotkey.Modifiers.Contains("Alt");
        HotkeyShift.IsChecked = settings.Hotkey.Modifiers.Contains("Shift");
        HotkeyKey.Text = settings.Hotkey.Key;

        // 其他
        var logLevels = new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };
        foreach (var l in logLevels) LogLevelComboBox.Items.Add(l);
        LogLevelComboBox.SelectedItem = settings.Logging.Level;
        LogFile.Text = settings.Logging.File;
        PythonRuntimePath.Text = settings.Python.RuntimePath;

        var themes = new[] { "system", "light", "dark" };
        foreach (var t in themes) ThemeComboBox.Items.Add(t);
        ThemeComboBox.SelectedItem = settings.Other.Theme;

        var fontModes = new[] { "auto", "fit-width", "custom" };
        foreach (var m in fontModes) FontSizeModeComboBox.Items.Add(m);
        FontSizeModeComboBox.SelectedItem = settings.Other.FontSizeMode;
        CustomFontSizeTextBox.Text = settings.Other.CustomFontSize.ToString();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var settings = _configManager.Settings;

        // OCR
        settings.Ocr.ActiveEngine = OcrEngineComboBox.SelectedItem?.ToString() ?? "PaddleOCR";
        settings.Ocr.Engines["PaddleOCR"] = new Dictionary<string, string>
        {
            ["modelPath"] = PaddleModelPath.Text
        };
        settings.Ocr.Engines["RemoteOCR"] = new Dictionary<string, string>
        {
            ["endpoint"] = RemoteOcrEndpoint.Text,
            ["apiKey"] = RemoteOcrApiKey.Text
        };
        var fb = OcrFallbackComboBox.SelectedItem?.ToString();
        settings.Ocr.FallbackEngine = fb == "(无)" ? null : fb;

        // 翻译
        settings.Translation.ActiveEngine = TranslationEngineComboBox.SelectedItem?.ToString() ?? "DeepL";
        settings.Translation.Engines["DeepL"] = new Dictionary<string, string>
        {
            ["apiKey"] = DeepLApiKey.Text,
            ["freeTier"] = DeepLFreeTier.IsChecked == true ? "true" : "false"
        };
        settings.Translation.Engines["Baidu"] = new Dictionary<string, string>
        {
            ["appId"] = BaiduAppId.Text,
            ["secret"] = BaiduSecret.Text
        };
        settings.Translation.Engines["OpenAI"] = new Dictionary<string, string>
        {
            ["apiKey"] = OpenAIApiKey.Text,
            ["model"] = OpenAIModel.Text
        };
        settings.Translation.Engines["Microsoft"] = new Dictionary<string, string>
        {
            ["apiKey"] = MicrosoftApiKey.Text,
            ["region"] = MicrosoftRegion.Text,
            ["endpoint"] = MicrosoftEndpoint.Text
        };

        // 语言
        settings.Language.Source = SourceLanguageComboBox.SelectedItem?.ToString() ?? "auto";
        settings.Language.Target = TargetLanguageComboBox.SelectedItem?.ToString() ?? "zh-CN";

        // 热键
        var modifiers = new List<string>();
        if (HotkeyCtrl.IsChecked == true) modifiers.Add("Ctrl");
        if (HotkeyAlt.IsChecked == true) modifiers.Add("Alt");
        if (HotkeyShift.IsChecked == true) modifiers.Add("Shift");
        settings.Hotkey.Modifiers = modifiers.ToArray();
        settings.Hotkey.Key = HotkeyKey.Text;

        // 其他
        settings.Logging.Level = LogLevelComboBox.SelectedItem?.ToString() ?? "Information";
        settings.Logging.File = LogFile.Text;
        settings.Python.RuntimePath = PythonRuntimePath.Text;

        settings.Other.Theme = ThemeComboBox.SelectedItem?.ToString() ?? "system";
        settings.Other.FontSizeMode = FontSizeModeComboBox.SelectedItem?.ToString() ?? "auto";
        if (int.TryParse(CustomFontSizeTextBox.Text, out var fontSize))
            settings.Other.CustomFontSize = fontSize;

        // 保存后立即应用主题
        ThemeManager.SetTheme(settings.Other.Theme);

        Log.Information("设置保存: OCR={Ocr}, Translation={Trans}, Source={Src}, Target={Tgt}",
            settings.Ocr.ActiveEngine, settings.Translation.ActiveEngine,
            settings.Language.Source, settings.Language.Target);
        _configManager.Save();
        MessageBox.Show(
            "设置已保存。\n\n" +
            "以下设置立即生效：语言、OCR/翻译引擎选择、API Key。\n" +
            "以下设置需要重启应用：热键、日志级别、日志文件路径、Python 路径、OCR 模型路径。",
            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

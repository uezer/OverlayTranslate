using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace OverlayTranslate.Controls;

public partial class FloatingToolbar : UserControl
{
    private string _originalText = "";
    private string _translatedText = "";
    private bool _showingOriginal;
    private bool _initialized;

    public event Action? OnReselect;
    public event Action? OnExit;
    public event Action<string, string>? OnLanguageChanged;
    public event Action<string, string>? OnEngineChanged;
    public event Action<bool>? OnShowOriginalToggled;

    public FloatingToolbar()
    {
        InitializeComponent();
        LoadDefaultLanguages();

        // 延迟注册事件，避免初始化期间触发
        _initialized = true;

        SourceLanguageComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized)
                OnLanguageChanged?.Invoke("source", SourceLanguageComboBox.SelectedItem?.ToString() ?? "");
        };
        TargetLanguageComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized)
                OnLanguageChanged?.Invoke("target", TargetLanguageComboBox.SelectedItem?.ToString() ?? "");
        };
        OcrEngineComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized)
                OnEngineChanged?.Invoke("ocr", OcrEngineComboBox.SelectedItem?.ToString() ?? "");
        };
        TranslationEngineComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized)
                OnEngineChanged?.Invoke("translation", TranslationEngineComboBox.SelectedItem?.ToString() ?? "");
        };
    }

    public void SetEngines(string[] ocrEngines, string[] translationEngines)
    {
        OcrEngineComboBox.Items.Clear();
        foreach (var engine in ocrEngines)
            OcrEngineComboBox.Items.Add(engine);
        if (OcrEngineComboBox.Items.Count > 0)
            OcrEngineComboBox.SelectedIndex = 0;

        TranslationEngineComboBox.Items.Clear();
        foreach (var engine in translationEngines)
            TranslationEngineComboBox.Items.Add(engine);
        if (TranslationEngineComboBox.Items.Count > 0)
            TranslationEngineComboBox.SelectedIndex = 0;
    }

    public void SetData(string originalText, string translatedText)
    {
        _originalText = originalText;
        _translatedText = translatedText;
        _showingOriginal = false;
        ShowOriginalButton.Content = "显示原文";
    }

    public string GetSourceLanguage() =>
        SourceLanguageComboBox.SelectedItem?.ToString() ?? "auto";

    public string GetTargetLanguage() =>
        TargetLanguageComboBox.SelectedItem?.ToString() ?? "zh";

    public string GetSelectedOcrEngine() =>
        OcrEngineComboBox.SelectedItem?.ToString() ?? "";

    public string GetSelectedTranslationEngine() =>
        TranslationEngineComboBox.SelectedItem?.ToString() ?? "";

    private void LoadDefaultLanguages()
    {
        string[] languages = ["auto", "zh", "en", "ja", "ko", "fr", "de", "es", "ru", "pt", "it"];
        foreach (var lang in languages)
        {
            SourceLanguageComboBox.Items.Add(lang);
            TargetLanguageComboBox.Items.Add(lang);
        }
        SourceLanguageComboBox.SelectedIndex = 0;
        TargetLanguageComboBox.SelectedIndex = 1;
    }

    private void OnReselectClick(object sender, RoutedEventArgs e) => OnReselect?.Invoke();
    private void OnExitClick(object sender, RoutedEventArgs e) => OnExit?.Invoke();

    private void OnShowOriginalClick(object sender, RoutedEventArgs e)
    {
        _showingOriginal = !_showingOriginal;
        ShowOriginalButton.Content = _showingOriginal ? "显示译文" : "显示原文";
        OnShowOriginalToggled?.Invoke(_showingOriginal);
    }

    private void OnCopyOriginalClick(object sender, RoutedEventArgs e)
    {
        TrySetClipboard(_originalText);
    }

    private void OnCopyTranslatedClick(object sender, RoutedEventArgs e)
    {
        TrySetClipboard(_translatedText);
    }

    private static void TrySetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // 剪贴板被其他进程锁定，忽略
        }
    }
}

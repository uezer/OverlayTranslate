using System.Windows;
using System.Windows.Controls;

namespace OverlayTranslate.Controls;

/// <summary>
/// 浮动工具栏，提供语言/引擎选择和操作按钮。
/// </summary>
public partial class FloatingToolbar : UserControl
{
    private string _originalText = "";
    private string _translatedText = "";
    private bool _showingOriginal;

    /// <summary>
    /// 点击"重选"按钮时触发。
    /// </summary>
    public event Action? OnReselect;

    /// <summary>
    /// 点击"退出"按钮时触发。
    /// </summary>
    public event Action? OnExit;

    /// <summary>
    /// 语言选择变更时触发（参数：源语言，目标语言）。
    /// </summary>
    public event Action<string, string>? OnLanguageChanged;

    /// <summary>
    /// 引擎选择变更时触发（参数：OCR引擎名，翻译引擎名）。
    /// </summary>
    public event Action<string, string>? OnEngineChanged;

    /// <summary>
    /// 切换显示原文/译文时触发（参数：true 表示显示原文）。
    /// </summary>
    public event Action<bool>? OnShowOriginalToggled;

    public FloatingToolbar()
    {
        InitializeComponent();
        LoadDefaultLanguages();

        SourceLanguageComboBox.SelectionChanged += (_, e) =>
            OnLanguageChanged?.Invoke("source", SourceLanguageComboBox.SelectedItem?.ToString() ?? "");
        TargetLanguageComboBox.SelectionChanged += (_, e) =>
            OnLanguageChanged?.Invoke("target", TargetLanguageComboBox.SelectedItem?.ToString() ?? "");
        OcrEngineComboBox.SelectionChanged += (_, e) =>
            OnEngineChanged?.Invoke("ocr", OcrEngineComboBox.SelectedItem?.ToString() ?? "");
        TranslationEngineComboBox.SelectionChanged += (_, e) =>
            OnEngineChanged?.Invoke("translation", TranslationEngineComboBox.SelectedItem?.ToString() ?? "");
    }

    /// <summary>
    /// 设置 OCR 和翻译引擎列表。
    /// </summary>
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

    /// <summary>
    /// 设置 OCR 结果和翻译结果数据。
    /// </summary>
    public void SetData(string originalText, string translatedText)
    {
        _originalText = originalText;
        _translatedText = translatedText;
        _showingOriginal = false;
        ShowOriginalButton.Content = "显示原文";
    }

    /// <summary>
    /// 获取当前选中的源语言。
    /// </summary>
    public string GetSourceLanguage()
    {
        return SourceLanguageComboBox.SelectedItem?.ToString() ?? "auto";
    }

    /// <summary>
    /// 获取当前选中的目标语言。
    /// </summary>
    public string GetTargetLanguage()
    {
        return TargetLanguageComboBox.SelectedItem?.ToString() ?? "zh";
    }

    /// <summary>
    /// 获取当前选中的 OCR 引擎名。
    /// </summary>
    public string GetSelectedOcrEngine()
    {
        return OcrEngineComboBox.SelectedItem?.ToString() ?? "";
    }

    /// <summary>
    /// 获取当前选中的翻译引擎名。
    /// </summary>
    public string GetSelectedTranslationEngine()
    {
        return TranslationEngineComboBox.SelectedItem?.ToString() ?? "";
    }

    private void LoadDefaultLanguages()
    {
        // 常用语言列表
        string[] languages = ["auto", "zh", "en", "ja", "ko", "fr", "de", "es", "ru", "pt", "it"];
        foreach (var lang in languages)
        {
            SourceLanguageComboBox.Items.Add(lang);
            TargetLanguageComboBox.Items.Add(lang);
        }

        SourceLanguageComboBox.SelectedIndex = 0; // auto
        TargetLanguageComboBox.SelectedIndex = 1; // zh
    }

    private void OnReselectClick(object sender, RoutedEventArgs e)
    {
        OnReselect?.Invoke();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        OnExit?.Invoke();
    }

    private void OnShowOriginalClick(object sender, RoutedEventArgs e)
    {
        _showingOriginal = !_showingOriginal;
        ShowOriginalButton.Content = _showingOriginal ? "显示译文" : "显示原文";
        OnShowOriginalToggled?.Invoke(_showingOriginal);
    }

    private void OnCopyOriginalClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_originalText))
        {
            Clipboard.SetText(_originalText);
        }
    }

    private void OnCopyTranslatedClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_translatedText))
        {
            Clipboard.SetText(_translatedText);
        }
    }
}

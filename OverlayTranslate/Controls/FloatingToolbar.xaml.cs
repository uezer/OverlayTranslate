using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OverlayTranslate.Models;
using OverlayTranslate.Windows;

namespace OverlayTranslate.Controls;

public partial class FloatingToolbar : UserControl
{
    private string _originalText = "";
    private string _translatedText = "";
    private OverlayViewMode _currentViewMode = OverlayViewMode.OriginalText;
    private bool _initialized;

    public event Action? OnReselect;
    public event Action? OnExit;
    public event Action<string, string>? OnLanguageChanged;
    public event Action<string, string>? OnEngineChanged;
    public event Action<OverlayViewMode>? OnViewModeChanged;
    public event Action? OnDragStarted;
    public event Action? OnShowOriginalImage;
    public event Action<bool>? OnOriginalBgFillChanged;
    public event Action<bool>? OnTranslatedBgFillChanged;

    private bool _isDragging;
    private Point _dragStart;
    private bool _eventsSuspended;

    public FloatingToolbar()
    {
        InitializeComponent();
        LoadDefaultLanguages();

        // 延迟注册事件，避免初始化期间触发
        _initialized = true;

        SourceLanguageComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                OnLanguageChanged?.Invoke("source", SourceLanguageComboBox.SelectedItem?.ToString() ?? "");
        };
        TargetLanguageComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                OnLanguageChanged?.Invoke("target", TargetLanguageComboBox.SelectedItem?.ToString() ?? "");
        };
        OcrEngineComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                OnEngineChanged?.Invoke("ocr", OcrEngineComboBox.SelectedItem?.ToString() ?? "");
        };
        TranslationEngineComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
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
    }

    public void SetViewMode(OverlayViewMode mode)
    {
        _currentViewMode = mode;
        ViewModeButton.Content = mode switch
        {
            OverlayViewMode.OriginalText => "原文",
            OverlayViewMode.TranslatedText => "译文",
            _ => "原文"
        };
    }

    public bool IsOriginalBgFillEnabled => OriginalBgFillCheckBox.IsChecked == true;
    public bool IsTranslatedBgFillEnabled => TranslatedBgFillCheckBox.IsChecked == true;

    public void SuspendEvents() => _eventsSuspended = true;
    public void ResumeEvents() => _eventsSuspended = false;

    public void SetLoading(bool loading)
    {
        ProgressBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    public string GetSourceLanguage() =>
        SourceLanguageComboBox.SelectedItem?.ToString() ?? "auto";

    public string GetTargetLanguage() =>
        TargetLanguageComboBox.SelectedItem?.ToString() ?? "zh";

    public void SetSourceLanguage(string lang)
    {
        var idx = SourceLanguageComboBox.Items.IndexOf(lang);
        if (idx >= 0) SourceLanguageComboBox.SelectedIndex = idx;
    }

    public void SetTargetLanguage(string lang)
    {
        var idx = TargetLanguageComboBox.Items.IndexOf(lang);
        if (idx >= 0) TargetLanguageComboBox.SelectedIndex = idx;
    }

    public void SetSelectedOcrEngine(string name)
    {
        var idx = OcrEngineComboBox.Items.IndexOf(name);
        if (idx >= 0) OcrEngineComboBox.SelectedIndex = idx;
    }

    public void SetSelectedTranslationEngine(string name)
    {
        var idx = TranslationEngineComboBox.Items.IndexOf(name);
        if (idx >= 0) TranslationEngineComboBox.SelectedIndex = idx;
    }

    public string GetSelectedOcrEngine() =>
        OcrEngineComboBox.SelectedItem?.ToString() ?? "";

    public string GetSelectedTranslationEngine() =>
        TranslationEngineComboBox.SelectedItem?.ToString() ?? "";

    private void LoadDefaultLanguages()
    {
        string[] languages = ["auto", "zh", "zh-CN", "en", "ja", "ko", "fr", "de", "es", "ru", "pt", "it"];
        foreach (var lang in languages)
        {
            SourceLanguageComboBox.Items.Add(lang);
            TargetLanguageComboBox.Items.Add(lang);
        }
        SourceLanguageComboBox.SelectedIndex = 0;
        TargetLanguageComboBox.SelectedIndex = 1;
    }

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
        var newLeft = currentLeft + dx;
        var newTop = currentTop + dy;

        var parent = Parent as FrameworkElement;
        if (parent != null)
        {
            newLeft = Math.Max(0, Math.Min(newLeft, parent.ActualWidth - ActualWidth));
            newTop = Math.Max(0, Math.Min(newTop, parent.ActualHeight - ActualHeight));
        }

        Canvas.SetLeft(this, newLeft);
        Canvas.SetTop(this, newTop);
        _dragStart = currentPos;
    }

    private void OnBorderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ((Border)sender).ReleaseMouseCapture();
    }

    private void OnReselectClick(object sender, RoutedEventArgs e) => OnReselect?.Invoke();
    private void OnExitClick(object sender, RoutedEventArgs e) => OnExit?.Invoke();

    // 视图切换：原文 ↔ 译文
    private void OnViewModeClick(object sender, RoutedEventArgs e)
    {
        _currentViewMode = _currentViewMode switch
        {
            OverlayViewMode.OriginalText => OverlayViewMode.TranslatedText,
            OverlayViewMode.TranslatedText => OverlayViewMode.OriginalText,
            _ => OverlayViewMode.OriginalText
        };
        ViewModeButton.Content = _currentViewMode switch
        {
            OverlayViewMode.OriginalText => "原文",
            OverlayViewMode.TranslatedText => "译文",
            _ => "原文"
        };
        OnViewModeChanged?.Invoke(_currentViewMode);
    }

    // 扩展面板切换
    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        ExpandPanel.Visibility = ExpandPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        ExpandButton.Content = ExpandPanel.Visibility == Visibility.Visible ? "▲" : "▼";
    }

    // 显示原图
    private void OnShowOriginalImageClick(object sender, RoutedEventArgs e)
    {
        _currentViewMode = OverlayViewMode.OriginalImage;
        ViewModeButton.Content = "原文"; // 按钮保持显示"原文"，原图是临时查看
        OnViewModeChanged?.Invoke(OverlayViewMode.OriginalImage);
    }

    // 原文底色覆盖开关
    private void OnOriginalBgFillClick(object sender, RoutedEventArgs e)
    {
        OnOriginalBgFillChanged?.Invoke(OriginalBgFillCheckBox.IsChecked == true);
    }

    // 译文底色覆盖开关
    private void OnTranslatedBgFillClick(object sender, RoutedEventArgs e)
    {
        OnTranslatedBgFillChanged?.Invoke(TranslatedBgFillCheckBox.IsChecked == true);
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

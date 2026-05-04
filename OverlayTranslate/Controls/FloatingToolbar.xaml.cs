using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OverlayTranslate.Models;
using OverlayTranslate.ViewModels;

namespace OverlayTranslate.Controls;

public partial class FloatingToolbar : UserControl
{
    private readonly FloatingToolbarViewModel _vm;
    private bool _initialized;
    private bool _isDragging;
    private Point _dragStart;
    private bool _eventsSuspended;

    public FloatingToolbarViewModel ViewModel => _vm;

    public FloatingToolbar()
    {
        _vm = new FloatingToolbarViewModel();
        DataContext = _vm;

        InitializeComponent();
        LoadDefaultLanguages();

        _initialized = true;

        SourceLanguageComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                _vm.NotifyLanguageChanged("source", SourceLanguageComboBox.SelectedItem?.ToString() ?? "");
        };
        TargetLanguageComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                _vm.NotifyLanguageChanged("target", TargetLanguageComboBox.SelectedItem?.ToString() ?? "");
        };
        OcrEngineComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                _vm.NotifyEngineChanged("ocr", OcrEngineComboBox.SelectedItem?.ToString() ?? "");
        };
        TranslationEngineComboBox.SelectionChanged += (_, _) =>
        {
            if (_initialized && !_eventsSuspended)
                _vm.NotifyEngineChanged("translation", TranslationEngineComboBox.SelectedItem?.ToString() ?? "");
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

    public bool IsOriginalBgFillEnabled => _vm.IsOriginalBgFillEnabled;
    public bool IsTranslatedBgFillEnabled => _vm.IsTranslatedBgFillEnabled;

    public void SuspendEvents() => _eventsSuspended = true;
    public void ResumeEvents() => _eventsSuspended = false;

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

    // 拖拽逻辑
    private void OnBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(Parent as UIElement);
        ((Border)sender).CaptureMouse();
        _vm.NotifyDragStarted();
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

    // CheckBox 点击：转发到 VM
    private void OnOriginalBgFillClick(object sender, RoutedEventArgs e)
    {
        _vm.SetOriginalBgFill(OriginalBgFillCheckBox.IsChecked == true);
    }

    private void OnTranslatedBgFillClick(object sender, RoutedEventArgs e)
    {
        _vm.SetTranslatedBgFill(TranslatedBgFillCheckBox.IsChecked == true);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverlayTranslate.Controls;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Models;
using OverlayTranslate.Services;
using OverlayTranslate.ViewModels;
using OverlayTranslate.Localization;
using Serilog;

namespace OverlayTranslate.Windows;

public partial class OverlayWindow : Window
{
    private readonly OverlayWindowViewModel _vm;
    private readonly ScreenshotService _screenshotService;
    private readonly TranslationPipeline _pipeline;

    private Point _selectionStart;
    private bool _autoPositionToolbar = true;

    public OverlayWindow(
        OverlayWindowViewModel viewModel,
        ScreenshotService screenshotService,
        TranslationPipeline pipeline)
    {
        _vm = viewModel;
        _screenshotService = screenshotService;
        _pipeline = pipeline;

        InitializeComponent();

        SizeChanged += (_, args) =>
        {
            Mask.Width = args.NewSize.Width;
            Mask.Height = args.NewSize.Height;
        };

        // 工具栏事件绑定（通过 ViewModel）
        var toolbarVm = Toolbar.ViewModel;
        toolbarVm.OnReselect += HandleReselect;
        toolbarVm.OnExit += ExitOverlay;
        toolbarVm.OnDragStarted += () => _autoPositionToolbar = false;
        toolbarVm.OnViewModeChanged += mode => ApplyViewMode(mode);
        toolbarVm.OnOriginalBgFillChanged += _ => ApplyViewMode(_vm.ViewMode);
        toolbarVm.OnTranslatedBgFillChanged += _ => ApplyViewMode(_vm.ViewMode);
        toolbarVm.OnLanguageChanged += (which, value) =>
        {
            if (which == "source") _vm.SourceLanguage = value;
            else _vm.TargetLanguage = value;
            if (_vm.State == OverlayState.Result) RerunTranslation();
        };
        toolbarVm.OnEngineChanged += (which, value) =>
        {
            if (which == "ocr")
            {
                _vm.SwitchOcrEngine(value);
                if (_vm.State == OverlayState.Result) RerunAll();
            }
            else if (which == "translation")
            {
                _vm.SwitchTranslationEngine(value);
                if (_vm.State == OverlayState.Result) RerunTranslation();
            }
        };

        // 初始化工具栏引擎列表
        var ocrNames = _pipeline.GetAvailableOcrEngines();
        var transNames = _pipeline.GetAvailableTranslationEngines();

        Toolbar.SuspendEvents();
        Toolbar.SetEngines(ocrNames, transNames);
        Toolbar.SetSelectedOcrEngine(_vm.CurrentOcrEngineName);
        Toolbar.SetSelectedTranslationEngine(_vm.CurrentTranslationEngineName);
        Toolbar.SetSourceLanguage(_vm.SourceLanguage);
        Toolbar.SetTargetLanguage(_vm.TargetLanguage);
        Toolbar.ResumeEvents();

        // 最小化/恢复模式：避免首次 Show 时最大化动画导致白屏闪烁
        Loaded += (_, _) => WindowState = WindowState.Maximized;
        WindowState = WindowState.Minimized;
    }

    // ============ 显示覆盖层 ============

    public void ShowForSelection()
    {
        try
        {
            var screenshotData = _screenshotService.CaptureFullScreen();
            Log.Information("ShowForSelection: screenshot {Size} bytes", screenshotData.Length);

            var bitmapImage = BytesToBitmapImage(screenshotData);
            _vm.ScreenshotData = screenshotData;
            _vm.ScreenshotDpiX = bitmapImage.DpiX > 0 ? bitmapImage.DpiX : 96;
            _vm.ScreenshotDpiY = bitmapImage.DpiY > 0 ? bitmapImage.DpiY : 96;
            BackgroundImage.Source = bitmapImage;

            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            TextOverlayCanvas.Children.Clear();
            TextOverlayCanvas.IsHitTestVisible = false;
            Toolbar.Visibility = Visibility.Collapsed;
            _vm.State = OverlayState.Selecting;

            Show();
            Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示覆盖层失败");
            _vm.State = OverlayState.Idle;
        }
    }

    // ============ 鼠标事件 ============

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_vm.State != OverlayState.Selecting) return;
        _selectionStart = e.GetPosition(RootGrid);
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_vm.State != OverlayState.Selecting || !IsMouseCaptured) return;

        var currentPoint = e.GetPosition(RootGrid);
        _vm.CurrentSelection = new Rect(
            Math.Min(_selectionStart.X, currentPoint.X),
            Math.Min(_selectionStart.Y, currentPoint.Y),
            Math.Abs(currentPoint.X - _selectionStart.X),
            Math.Abs(currentPoint.Y - _selectionStart.Y));

        Mask.SetSelection(_vm.CurrentSelection);
        SelectionLayer.UpdateSelection(_vm.CurrentSelection);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_vm.State != OverlayState.Selecting || !IsMouseCaptured) return;
        ReleaseMouseCapture();

        var currentPoint = e.GetPosition(RootGrid);
        _vm.CurrentSelection = new Rect(
            Math.Min(_selectionStart.X, currentPoint.X),
            Math.Min(_selectionStart.Y, currentPoint.Y),
            Math.Abs(currentPoint.X - _selectionStart.X),
            Math.Abs(currentPoint.Y - _selectionStart.Y));

        if (_vm.CurrentSelection.Width < 5 || _vm.CurrentSelection.Height < 5)
        {
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            return;
        }

        Mask.SetSelection(_vm.CurrentSelection);
        SelectionLayer.UpdateSelection(_vm.CurrentSelection);
        _vm.CancelAndStart();
        _ = ProcessSelectionAsync(_vm.CurrentSelection);
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.CancelAndStart();
            ExitOverlay();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _vm.CancelAndStart();
        ExitOverlay();
        e.Handled = true;
        base.OnMouseRightButtonDown(e);
    }

    // ============ 核心处理流程 ============

    private async Task ProcessSelectionAsync(Rect selection)
    {
        if (_vm.State == OverlayState.Exiting || _vm.ScreenshotData == null) return;

        _vm.State = OverlayState.Processing;
        Toolbar.ViewModel.IsLoading = true;

        try
        {
            // === 阶段 1：OCR + 原文覆盖 ===
            _vm.Cts.Token.ThrowIfCancellationRequested();
            var ocrEngine = _pipeline.GetOcrEngine(_vm.CurrentOcrEngineName);
            var regionImage = _screenshotService.CropRegion(_vm.ScreenshotData, selection);

            var ocrResult = await ocrEngine.RecognizeAsync(regionImage, ct: _vm.Cts.Token);
            _vm.Cts.Token.ThrowIfCancellationRequested();

            if (ocrResult.TextBlocks.Count == 0)
            {
                _vm.State = OverlayState.Selecting;
                return;
            }

            _vm.OriginalText = ocrResult.FullText;
            _vm.LastOcrTextBlocks = ocrResult.TextBlocks;
            _vm.CurrentSelection = selection;
            Log.Information("OCR: {Count} blocks, {Text}", ocrResult.TextBlocks.Count,
                _vm.OriginalText.Length > 50 ? _vm.OriginalText[..50] + "..." : _vm.OriginalText);

            // 显示原文覆盖（即时反馈）
            var dpiScaleX = _vm.ScreenshotDpiX / 96.0;
            var dpiScaleY = _vm.ScreenshotDpiY / 96.0;

            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            TextOverlayCanvas.IsHitTestVisible = true;
            PopulateTextOverlays(ocrResult.TextBlocks, selection, dpiScaleX, dpiScaleY);

            _vm.ViewMode = OverlayViewMode.OriginalText;
            Toolbar.ViewModel.SetData(_vm.OriginalText, "");
            Toolbar.ViewModel.SetViewMode(OverlayViewMode.OriginalText);
            Toolbar.Visibility = Visibility.Visible;
            PositionToolbar(selection);
            _vm.State = OverlayState.Result;

            // === 阶段 2：翻译 + 译文覆盖 ===
            Toolbar.ViewModel.IsLoading = true;
            var sourceLang = _vm.SourceLanguage;
            var targetLang = _vm.TargetLanguage;

            var result = await _pipeline.TranslateBlocksAsync(
                _vm.ScreenshotData, selection,
                ocrResult.TextBlocks, _vm.OriginalText,
                _vm.ScreenshotDpiX, _vm.ScreenshotDpiY,
                _vm.CurrentTranslationEngineName,
                sourceLang, targetLang, _vm.Cts.Token);

            _vm.TranslatedText = result.TranslatedText;
            _vm.TranslatedBlocks = result.TranslatedBlocks;
            _vm.FilledImageBytes = result.FilledImageBytes;
            _vm.TranslatedStyle = result.TranslatedStyle;
            Log.Information("翻译: {Text}", _vm.TranslatedText.Length > 50 ? _vm.TranslatedText[..50] + "..." : _vm.TranslatedText);

            Toolbar.ViewModel.SetData(_vm.OriginalText, _vm.TranslatedText);
            ApplyViewMode(OverlayViewMode.TranslatedText);
            Toolbar.ViewModel.SetViewMode(OverlayViewMode.TranslatedText);
            Toolbar.ViewModel.IsLoading = false;
        }
        catch (OperationCanceledException)
        {
            Toolbar.ViewModel.IsLoading = false;
        }
        catch (Exception ex)
        {
            Toolbar.ViewModel.IsLoading = false;
            Log.Error(ex, "处理选区失败");
            _vm.State = OverlayState.Selecting;

            Hide();
            MessageBox.Show(string.Format(LocManager.Get("Msg_ProcessFailed_Body"), ex.Message),
                LocManager.Get("App_Name"), MessageBoxButton.OK, MessageBoxImage.Warning);
            Show();
            Activate();
        }
    }

    // ============ 重新处理 ============

    private void RerunAll()
    {
        if (_vm.State != OverlayState.Result || _vm.ScreenshotData == null) return;
        _vm.FilledImageBytes = null;
        _vm.CancelAndStart();
        _ = ProcessSelectionAsync(_vm.CurrentSelection);
    }

    private void RerunTranslation()
    {
        if (_vm.State != OverlayState.Result || _vm.ScreenshotData == null) return;
        _vm.CancelAndStart();
        _ = ReTranslateAsync();
    }

    private async Task ReTranslateAsync()
    {
        Toolbar.ViewModel.IsLoading = true;
        try
        {
            var sourceLang = _vm.SourceLanguage;
            var targetLang = _vm.TargetLanguage;

            var result = await _pipeline.TranslateBlocksAsync(
                _vm.ScreenshotData!, _vm.CurrentSelection,
                _vm.LastOcrTextBlocks!, _vm.OriginalText,
                _vm.ScreenshotDpiX, _vm.ScreenshotDpiY,
                _vm.CurrentTranslationEngineName,
                sourceLang, targetLang, _vm.Cts.Token);

            _vm.TranslatedText = result.TranslatedText;
            _vm.TranslatedBlocks = result.TranslatedBlocks;
            if (_vm.FilledImageBytes == null)
                _vm.FilledImageBytes = result.FilledImageBytes;
            _vm.TranslatedStyle = result.TranslatedStyle;

            Toolbar.ViewModel.SetData(_vm.OriginalText, _vm.TranslatedText);
            if (_vm.ViewMode == OverlayViewMode.TranslatedText)
                ApplyViewMode(OverlayViewMode.TranslatedText);
            Toolbar.ViewModel.IsLoading = false;
        }
        catch (OperationCanceledException) { Toolbar.ViewModel.IsLoading = false; }
        catch (Exception ex)
        {
            Toolbar.ViewModel.IsLoading = false;
            Log.Error(ex, "重新翻译失败");
        }
    }

    // ============ 视图切换 ============

    private void ApplyViewMode(OverlayViewMode mode)
    {
        _vm.ViewMode = mode;
        TextOverlayCanvas.Children.Clear();

        var dpiScaleX = _vm.ScreenshotDpiX / 96.0;
        var dpiScaleY = _vm.ScreenshotDpiY / 96.0;

        switch (mode)
        {
            case OverlayViewMode.OriginalImage:
                if (_vm.ScreenshotData != null)
                    BackgroundImage.Source = BytesToBitmapImage(_vm.ScreenshotData);
                break;

            case OverlayViewMode.OriginalText:
                if (_vm.ScreenshotData != null)
                    BackgroundImage.Source = BytesToBitmapImage(_vm.ScreenshotData);
                if (_vm.LastOcrTextBlocks != null)
                    PopulateTextOverlays(_vm.LastOcrTextBlocks, _vm.CurrentSelection, dpiScaleX, dpiScaleY);
                break;

            case OverlayViewMode.TranslatedText:
                if (Toolbar.ViewModel.IsTranslatedBgFillEnabled && _vm.FilledImageBytes != null)
                    BackgroundImage.Source = BytesToBitmapImage(_vm.FilledImageBytes);
                else if (_vm.ScreenshotData != null)
                    BackgroundImage.Source = BytesToBitmapImage(_vm.ScreenshotData);
                PopulateTranslatedOverlays();
                break;
        }
    }

    // ============ TextBox 覆盖 ============

    private void PopulateTextOverlays(IReadOnlyList<Models.TextBlock> blocks, Rect selection,
        double dpiScaleX, double dpiScaleY)
    {
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            var dipX = selection.X + block.BoundingBox.X / dpiScaleX;
            var dipY = selection.Y + block.BoundingBox.Y / dpiScaleY;
            var dipW = block.BoundingBox.Width / dpiScaleX;
            var dipH = block.BoundingBox.Height / dpiScaleY;

            var fontSize = Math.Max(8, dipH * 0.75);
            var tb = CreateSelectableTextBox(block.Text, fontSize, dipW, dipH, Colors.White, true);
            Canvas.SetLeft(tb, dipX);
            Canvas.SetTop(tb, dipY);
            TextOverlayCanvas.Children.Add(tb);
        }
    }

    private void PopulateTranslatedOverlays()
    {
        if (_vm.TranslatedBlocks == null || _vm.TranslatedStyle == null) return;

        var dpiScaleX = _vm.ScreenshotDpiX / 96.0;
        var dpiScaleY = _vm.ScreenshotDpiY / 96.0;
        var style = _vm.TranslatedStyle;
        var bgFill = Toolbar.ViewModel.IsTranslatedBgFillEnabled;

        foreach (var (text, bbox) in _vm.TranslatedBlocks)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            var dipX = _vm.CurrentSelection.X + bbox.X / dpiScaleX;
            var dipY = _vm.CurrentSelection.Y + bbox.Y / dpiScaleY;
            var dipW = bbox.Width / dpiScaleX;
            var dipH = bbox.Height / dpiScaleY;

            var fontSize = Math.Max(8, dipH * 0.75);
            fontSize = StyleAnalyzer.ScaleFontSizeToFit(fontSize, dipW, text.Length);

            var tb = CreateSelectableTextBox(text, fontSize, dipW, dipH, style.TextColor, bgFill);
            Canvas.SetLeft(tb, dipX);
            Canvas.SetTop(tb, dipY);
            TextOverlayCanvas.Children.Add(tb);
        }
    }

    private static TextBox CreateSelectableTextBox(string text, double fontSize, double dipW, double dipH, Color textColor, bool bgFill)
    {
        var bgColor = bgFill
            ? (textColor == Colors.White
                ? Color.FromArgb(160, 0, 0, 0)
                : Color.FromArgb(160, 255, 255, 255))
            : Colors.Transparent;

        return new TextBox
        {
            Text = text,
            IsReadOnly = true,
            Background = new SolidColorBrush(bgColor),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(textColor),
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(2, 0, 2, 0),
            Width = dipW + 4,
            Height = dipH + 4,
            VerticalContentAlignment = VerticalAlignment.Top,
            Cursor = Cursors.IBeam
        };
    }

    // ============ 工具栏定位 ============

    private void PositionToolbar(Rect selection)
    {
        if (!_autoPositionToolbar) return;
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = Toolbar.DesiredSize;

        var screenW = ActualWidth;
        var screenH = ActualHeight;

        double x, y;

        y = selection.Bottom + 8;
        if (y + toolbarSize.Height > screenH)
        {
            y = selection.Top - toolbarSize.Height - 8;
            if (y < 0)
            {
                x = selection.Right + 8;
                y = selection.Y;
                if (x + toolbarSize.Width > screenW)
                {
                    x = selection.Left - toolbarSize.Width - 8;
                    if (x < 0) x = 8;
                }
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

        x = Math.Max(8, Math.Min(x, screenW - toolbarSize.Width - 8));
        y = Math.Max(8, Math.Min(y, screenH - toolbarSize.Height - 8));

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    // ============ 状态管理 ============

    private void HandleReselect()
    {
        _autoPositionToolbar = true;
        _vm.ClearForReselect();
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        TextOverlayCanvas.Children.Clear();
        TextOverlayCanvas.IsHitTestVisible = false;
        Toolbar.Visibility = Visibility.Collapsed;

        if (_vm.ScreenshotData != null)
            BackgroundImage.Source = BytesToBitmapImage(_vm.ScreenshotData);
    }

    private void ExitOverlay()
    {
        _vm.ClearForExit();
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        TextOverlayCanvas.Children.Clear();
        TextOverlayCanvas.IsHitTestVisible = false;
        Toolbar.Visibility = Visibility.Collapsed;
        BackgroundImage.Source = null;
        Hide();
    }

    // ============ 工具函数 ============

    private static BitmapImage BytesToBitmapImage(byte[] data)
    {
        var bitmapImage = new BitmapImage();
        using var stream = new System.IO.MemoryStream(data);
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Controls;
using OverlayTranslate.Engines;
using OverlayTranslate.Models;
using OverlayTranslate.Services;
using Serilog;

namespace OverlayTranslate.Windows;

public enum OverlayState
{
    Idle,
    Selecting,
    Processing,
    Result,
    Exiting
}

public partial class OverlayWindow : Window
{
    private readonly ScreenshotService _screenshotService;
    private readonly ImageProcessor _imageProcessor;
    private readonly TextRenderer _textRenderer;
    private readonly StyleAnalyzer _styleAnalyzer;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITranslationEngine _translationEngine;

    private OverlayState _state = OverlayState.Idle;
    private Point _selectionStart;
    private Rect _currentSelection;
    private byte[]? _screenshotData;
    private string _originalText = "";
    private string _translatedText = "";
    private ImageSource? _translationResultImage;
    private int _selectionGeneration;
    private double _screenshotDpiX = 96;
    private double _screenshotDpiY = 96;

    public OverlayWindow(
        ScreenshotService screenshotService,
        ImageProcessor imageProcessor,
        TextRenderer textRenderer,
        StyleAnalyzer styleAnalyzer,
        IOcrEngine ocrEngine,
        ITranslationEngine translationEngine)
    {
        _screenshotService = screenshotService;
        _imageProcessor = imageProcessor;
        _textRenderer = textRenderer;
        _styleAnalyzer = styleAnalyzer;
        _ocrEngine = ocrEngine;
        _translationEngine = translationEngine;

        InitializeComponent();

        // 窗口尺寸变化时同步 OverlayCanvas、Image 和 MaskLayer 的尺寸
        SizeChanged += (_, args) =>
        {
            var w = args.NewSize.Width;
            var h = args.NewSize.Height;
            Log.Information("SizeChanged: {W}x{H}", w, h);
            // Grid 自动填充窗口，BackgroundImage 会自动填充 Grid
            // OverlayCanvas 也会自动填充 Grid（作为 Grid 的第二个子元素）
            // 但 MaskLayer 是 Canvas 的子元素，需要显式设置尺寸
            Mask.Width = w;
            Mask.Height = h;
        };

        Toolbar.OnReselect += HandleReselect;
        Toolbar.OnExit += HandleExit;
        Toolbar.OnShowOriginalToggled += showOriginal =>
        {
            if (showOriginal && _screenshotData != null)
                ShowBackgroundImage(_screenshotData);
            else if (!showOriginal && _translationResultImage != null)
                ShowTranslationResult();
        };
        Toolbar.OnLanguageChanged += (which, value) =>
            Log.Debug("语言切换: {Which} = {Value}", which, value);
        Toolbar.OnEngineChanged += (which, value) =>
            Log.Debug("引擎切换: {Which} = {Value}", which, value);

        Toolbar.SetEngines(
            _ocrEngine.GetSupportedLanguages(),
            _translationEngine.GetSupportedLanguages());
    }

    public void ShowForSelection()
    {
        try
        {
            _screenshotData = _screenshotService.CaptureFullScreen();
            Log.Information("ShowForSelection: screenshot {Size} bytes", _screenshotData.Length);

            var bitmapImage = BytesToBitmapImage(_screenshotData);
            _screenshotDpiX = bitmapImage.DpiX > 0 ? bitmapImage.DpiX : 96;
            _screenshotDpiY = bitmapImage.DpiY > 0 ? bitmapImage.DpiY : 96;
            Log.Information("ShowForSelection: bitmapImage Pixel={PW}x{PH}, DIP={DW}x{DH}, DPI={DpiX}x{DpiY}",
                bitmapImage.PixelWidth, bitmapImage.PixelHeight, bitmapImage.Width, bitmapImage.Height,
                _screenshotDpiX, _screenshotDpiY);
            BackgroundImage.Source = bitmapImage;

            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            Toolbar.Visibility = Visibility.Collapsed;
            _state = OverlayState.Selecting;

            Show();
            Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示覆盖层失败");
            _state = OverlayState.Idle;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_state != OverlayState.Selecting) return;
        _selectionStart = e.GetPosition(RootGrid);
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_state != OverlayState.Selecting || !IsMouseCaptured) return;

        var currentPoint = e.GetPosition(RootGrid);
        _currentSelection = new Rect(
            Math.Min(_selectionStart.X, currentPoint.X),
            Math.Min(_selectionStart.Y, currentPoint.Y),
            Math.Abs(currentPoint.X - _selectionStart.X),
            Math.Abs(currentPoint.Y - _selectionStart.Y));

        Mask.SetSelection(_currentSelection);
        SelectionLayer.UpdateSelection(_currentSelection);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_state != OverlayState.Selecting || !IsMouseCaptured) return;
        ReleaseMouseCapture();

        var currentPoint = e.GetPosition(RootGrid);
        _currentSelection = new Rect(
            Math.Min(_selectionStart.X, currentPoint.X),
            Math.Min(_selectionStart.Y, currentPoint.Y),
            Math.Abs(currentPoint.X - _selectionStart.X),
            Math.Abs(currentPoint.Y - _selectionStart.Y));

        if (_currentSelection.Width < 5 || _currentSelection.Height < 5)
        {
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            return;
        }

        Mask.SetSelection(_currentSelection);
        SelectionLayer.UpdateSelection(_currentSelection);
        var generation = ++_selectionGeneration;
        _ = ProcessSelectionAsync(_currentSelection, generation);
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _selectionGeneration++; // 取消进行中的处理
            ExitOverlay();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _selectionGeneration++;
        ExitOverlay();
        e.Handled = true;
        base.OnMouseRightButtonDown(e);
    }

    private async Task ProcessSelectionAsync(Rect selection, int generation)
    {
        if (_state == OverlayState.Exiting || _screenshotData == null) return;

        _state = OverlayState.Processing;

        try
        {
            var regionImage = _screenshotService.CropRegion(_screenshotData, selection);
            if (_selectionGeneration != generation) return;
            Log.Information("CropRegion: {Size} bytes for region {X},{Y},{W},{H}", regionImage.Length, selection.X, selection.Y, selection.Width, selection.Height);

            var ocrResult = await _ocrEngine.RecognizeAsync(regionImage);
            if (_selectionGeneration != generation) return;

            if (ocrResult.TextBlocks.Count == 0)
            {
                _state = OverlayState.Selecting;
                return;
            }

            _originalText = ocrResult.FullText;
            Log.Information("OCR result: {Text}", _originalText.Length > 50 ? _originalText[..50] + "..." : _originalText);

            var sourceLang = Toolbar.GetSourceLanguage();
            var targetLang = Toolbar.GetTargetLanguage();
            var translationResult = await _translationEngine.TranslateAsync(
                _originalText, sourceLang, targetLang);
            if (_selectionGeneration != generation) return;

            _translatedText = translationResult.TranslatedText;
            Log.Information("Translation: {Text}", _translatedText.Length > 50 ? _translatedText[..50] + "..." : _translatedText);

            var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData, selection);
            // 将 OpenCV BGR Scalar 转换为 WPF Color
            var wpfBgColor = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp(bgColor.Val2, 0, 255),
                (byte)Math.Clamp(bgColor.Val1, 0, 255),
                (byte)Math.Clamp(bgColor.Val0, 0, 255));
            var styleInfo = _styleAnalyzer.Analyze(selection, _originalText, wpfBgColor);
            Log.Information("Background color sampled: B={B}, G={G}, R={R}", bgColor.Val0, bgColor.Val1, bgColor.Val2);

            var filledImage = _imageProcessor.FillRegion(_screenshotData, selection, bgColor);
            Log.Information("FillRegion done: {Size} bytes", filledImage.Length);

            // 使用原始截图的 DPI（FillRegion 的 PNG 编码会丢失 DPI 元数据）
            var resultImage = _textRenderer.RenderTranslatedText(
                filledImage, _translatedText, selection, styleInfo,
                _screenshotDpiX, _screenshotDpiY);
            Log.Information("RenderTranslatedText done: Pixel={PW}x{PH}", resultImage.PixelWidth, resultImage.PixelHeight);

            _translationResultImage = resultImage;
            BackgroundImage.Source = resultImage;
            Log.Information("BackgroundImage.Source set: Image.ActualSize={W}x{H}, Image.Visibility={Vis}, Grid.ActualSize={GW}x{GH}, OverlayCanvas.ActualSize={OW}x{OH}, Window.ActualSize={WW}x{WH}",
                BackgroundImage.ActualWidth, BackgroundImage.ActualHeight, BackgroundImage.Visibility,
                RootGrid.ActualWidth, RootGrid.ActualHeight,
                OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight,
                ActualWidth, ActualHeight);
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();

            Toolbar.SetData(_originalText, _translatedText);
            PositionToolbar(selection);
            Toolbar.Visibility = Visibility.Visible;

            _state = OverlayState.Result;
        }
        catch (Exception ex)
        {
            if (_selectionGeneration != generation) return;
            Log.Error(ex, "处理选区失败");
            _state = OverlayState.Selecting;

            // 先隐藏覆盖层，再显示 MessageBox，避免被遮挡
            Hide();
            MessageBox.Show($"处理失败: {ex.Message}\n\n请检查引擎配置（右键托盘图标 → 设置）。",
                "OverlayTranslate", MessageBoxButton.OK, MessageBoxImage.Warning);
            Show();
            Activate();
        }
    }

    private void PositionToolbar(Rect selection)
    {
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = Toolbar.DesiredSize;

        double x = selection.X;
        double y = selection.Bottom + 8;

        if (y + toolbarSize.Height > ActualHeight)
            y = selection.Top - toolbarSize.Height - 8;
        if (x + toolbarSize.Width > ActualWidth)
            x = ActualWidth - toolbarSize.Width - 8;
        if (x < 0) x = 8;
        if (y < 0) y = 8;

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    private void HandleReselect()
    {
        _selectionGeneration++; // 取消进行中的处理
        _state = OverlayState.Selecting;
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        Toolbar.Visibility = Visibility.Collapsed;

        if (_screenshotData != null)
            BackgroundImage.Source = BytesToBitmapImage(_screenshotData);
    }

    private void HandleExit() => ExitOverlay();

    private void ShowBackgroundImage(byte[] imageData)
    {
        BackgroundImage.Source = BytesToBitmapImage(imageData);
    }

    private void ShowTranslationResult()
    {
        if (_translationResultImage != null)
            BackgroundImage.Source = _translationResultImage;
    }

    private void ExitOverlay()
    {
        _state = OverlayState.Exiting;
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        Toolbar.Visibility = Visibility.Collapsed;
        BackgroundImage.Source = null;
        _screenshotData = null;
        Hide();
        _state = OverlayState.Idle;
    }

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

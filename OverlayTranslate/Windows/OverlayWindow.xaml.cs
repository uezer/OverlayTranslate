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

/// <summary>
/// 覆盖层状态。
/// </summary>
public enum OverlayState
{
    /// <summary>空闲状态，覆盖层未显示。</summary>
    Idle,
    /// <summary>正在选择区域。</summary>
    Selecting,
    /// <summary>正在处理（OCR + 翻译）。</summary>
    Processing,
    /// <summary>显示结果。</summary>
    Result,
    /// <summary>正在退出。</summary>
    Exiting
}

/// <summary>
/// 覆盖层窗口，支持全屏截图、区域选择、OCR 和翻译。
/// </summary>
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

        // 注册工具栏事件
        Toolbar.OnReselect += HandleReselect;
        Toolbar.OnExit += HandleExit;
        Toolbar.OnShowOriginalToggled += showOriginal =>
        {
            if (showOriginal && _screenshotData != null)
            {
                ShowBackgroundImage(_screenshotData);
            }
            else if (!showOriginal && _translationResultImage != null)
            {
                ShowTranslationResult();
            }
        };

        // 注册引擎列表
        Toolbar.SetEngines(
            _ocrEngine.GetSupportedLanguages(),
            _translationEngine.GetSupportedLanguages());
    }

    /// <summary>
    /// 显示覆盖层并进入选择模式。
    /// </summary>
    public void ShowForSelection()
    {
        try
        {
            // 截取全屏
            _screenshotData = _screenshotService.CaptureFullScreen();

            // 将截图显示为背景
            var bitmapImage = new BitmapImage();
            using (var stream = new System.IO.MemoryStream(_screenshotData))
            {
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
            }
            BackgroundImage.Source = bitmapImage;

            // 重置状态
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

        // 更新选区显示
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

        // 检查选区是否足够大
        if (_currentSelection.Width < 5 || _currentSelection.Height < 5)
        {
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            return;
        }

        // 更新遮罩并开始处理
        Mask.SetSelection(_currentSelection);
        SelectionLayer.UpdateSelection(_currentSelection);
        _ = ProcessSelectionAsync(_currentSelection);

        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc 或右键退出
        if (e.Key == Key.Escape)
        {
            ExitOverlay();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        ExitOverlay();
        e.Handled = true;
        base.OnMouseRightButtonDown(e);
    }

    /// <summary>
    /// 处理选区：OCR → 翻译 → 显示结果。
    /// </summary>
    private async Task ProcessSelectionAsync(Rect selection)
    {
        if (_state == OverlayState.Exiting || _screenshotData == null) return;

        _state = OverlayState.Processing;

        try
        {
            // 截取选区图像
            var regionImage = _screenshotService.CaptureRegion(selection);

            // OCR 识别
            var ocrResult = await _ocrEngine.RecognizeAsync(regionImage);
            if (ocrResult.TextBlocks.Count == 0)
            {
                _state = OverlayState.Selecting;
                return;
            }

            _originalText = ocrResult.FullText;

            // 翻译
            var sourceLang = Toolbar.GetSourceLanguage();
            var targetLang = Toolbar.GetTargetLanguage();
            var translationResult = await _translationEngine.TranslateAsync(
                _originalText, sourceLang, targetLang);

            _translatedText = translationResult.TranslatedText;

            // 样式分析与渲染
            var styleInfo = _styleAnalyzer.Analyze(selection, _originalText);

            // 采样背景色并填充原文字区域
            var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData, selection);
            var filledImage = _imageProcessor.FillRegion(_screenshotData, selection, bgColor);

            // 渲染翻译文本
            var resultImage = _textRenderer.RenderTranslatedText(
                filledImage, _translatedText, selection, styleInfo);

            // 更新显示
            _translationResultImage = resultImage;
            BackgroundImage.Source = resultImage;
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();

            // 显示工具栏
            Toolbar.SetData(_originalText, _translatedText);
            PositionToolbar(selection);
            Toolbar.Visibility = Visibility.Visible;

            _state = OverlayState.Result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理选区失败");
            _state = OverlayState.Selecting;
        }
    }

    /// <summary>
    /// 将工具栏定位在选区下方，自动避让屏幕边缘。
    /// </summary>
    private void PositionToolbar(Rect selection)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        // 测量工具栏所需尺寸
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = Toolbar.DesiredSize;

        // 默认放在选区下方
        double x = selection.X;
        double y = selection.Bottom + 8;

        // 如果超出底部，则放在选区上方
        if (y + toolbarSize.Height > screenHeight)
        {
            y = selection.Top - toolbarSize.Height - 8;
        }

        // 如果超出右侧，则向左偏移
        if (x + toolbarSize.Width > screenWidth)
        {
            x = screenWidth - toolbarSize.Width - 8;
        }

        // 确保不超出左侧
        if (x < 0) x = 8;

        // 确保不超出顶部
        if (y < 0) y = 8;

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    /// <summary>
    /// 重选：清除选区，回到选择状态。
    /// </summary>
    private void HandleReselect()
    {
        _state = OverlayState.Selecting;
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        Toolbar.Visibility = Visibility.Collapsed;

        // 恢复原始截图
        if (_screenshotData != null)
        {
            var bitmapImage = new BitmapImage();
            using var stream = new System.IO.MemoryStream(_screenshotData);
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            BackgroundImage.Source = bitmapImage;
        }
    }

    /// <summary>
    /// 退出覆盖层。
    /// </summary>
    private void HandleExit()
    {
        ExitOverlay();
    }

    /// <summary>
    /// 显示原始截图。
    /// </summary>
    private void ShowBackgroundImage(byte[] imageData)
    {
        var bitmapImage = new BitmapImage();
        using var stream = new System.IO.MemoryStream(imageData);
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        BackgroundImage.Source = bitmapImage;
    }

    /// <summary>
    /// 显示翻译结果图像。
    /// </summary>
    private void ShowTranslationResult()
    {
        if (_translationResultImage != null)
        {
            BackgroundImage.Source = _translationResultImage;
        }
    }

    /// <summary>
    /// 退出覆盖层，恢复到空闲状态。
    /// </summary>
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
}

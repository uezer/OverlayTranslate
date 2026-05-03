using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverlayTranslate.Controls;
using OverlayTranslate.Engines;
using OverlayTranslate.Infrastructure;
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
    private readonly Dictionary<string, IOcrEngine> _ocrEngines;
    private readonly Dictionary<string, ITranslationEngine> _translationEngines;
    private readonly ConfigManager _configManager;

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
    private bool _autoPositionToolbar = true;
    private string _currentOcrEngineName = "";
    private string _currentTranslationEngineName = "";
    private IReadOnlyList<Models.TextBlock>? _lastOcrTextBlocks;

    public OverlayWindow(
        ScreenshotService screenshotService,
        ImageProcessor imageProcessor,
        TextRenderer textRenderer,
        StyleAnalyzer styleAnalyzer,
        IOcrEngine ocrEngine,
        ITranslationEngine translationEngine,
        Dictionary<string, IOcrEngine> ocrEngines,
        Dictionary<string, ITranslationEngine> translationEngines,
        ConfigManager configManager)
    {
        _screenshotService = screenshotService;
        _imageProcessor = imageProcessor;
        _textRenderer = textRenderer;
        _styleAnalyzer = styleAnalyzer;
        _ocrEngine = ocrEngine;
        _translationEngine = translationEngine;
        _ocrEngines = ocrEngines;
        _translationEngines = translationEngines;
        _configManager = configManager;

        InitializeComponent();

        // 窗口尺寸变化时同步 OverlayCanvas、Image 和 MaskLayer 的尺寸
        SizeChanged += (_, args) =>
        {
            var w = args.NewSize.Width;
            var h = args.NewSize.Height;
            Log.Debug("SizeChanged: {W}x{H}", w, h);
            // Grid 自动填充窗口，BackgroundImage 会自动填充 Grid
            // OverlayCanvas 也会自动填充 Grid（作为 Grid 的第二个子元素）
            // 但 MaskLayer 是 Canvas 的子元素，需要显式设置尺寸
            Mask.Width = w;
            Mask.Height = h;
        };

        Toolbar.OnReselect += HandleReselect;
        Toolbar.OnExit += HandleExit;
        Toolbar.OnDragStarted += () => _autoPositionToolbar = false;
        Toolbar.OnShowOriginalToggled += showOriginal =>
        {
            if (showOriginal && _screenshotData != null)
                ShowBackgroundImage(_screenshotData);
            else if (!showOriginal && _translationResultImage != null)
                ShowTranslationResult();
        };
        Toolbar.OnLanguageChanged += (_, _) => RerunTranslation();
        Toolbar.OnEngineChanged += (which, value) =>
        {
            if (which == "ocr")
            {
                var name = UnmapOcrDisplayName(value);
                if (_ocrEngines.ContainsKey(name))
                {
                    _currentOcrEngineName = name;
                    Log.Information("切换 OCR 引擎: {Engine}", name);
                    RerunAll();
                }
            }
            else if (which == "translation")
            {
                var name = UnmapTranslationDisplayName(value);
                if (_translationEngines.ContainsKey(name))
                {
                    _currentTranslationEngineName = name;
                    Log.Information("切换翻译引擎: {Engine}", name);
                    RerunTranslation();
                }
            }
        };

        var ocrNames = _ocrEngines.Keys.ToArray();
        var transNames = _translationEngines.Keys.ToArray();
        _currentOcrEngineName = ocrNames.FirstOrDefault() ?? "";
        _currentTranslationEngineName = transNames.FirstOrDefault() ?? "";
        Toolbar.SetEngines(
            ocrNames.Select(MapOcrDisplayName).ToArray(),
            transNames.Select(MapTranslationDisplayName).ToArray());
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
            Log.Debug("ShowForSelection: bitmapImage Pixel={PW}x{PH}, DIP={DW}x{DH}, DPI={DpiX}x{DpiY}",
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
            Log.Debug("CropRegion: {Size} bytes for region {X},{Y},{W},{H}", regionImage.Length, selection.X, selection.Y, selection.Width, selection.Height);

            var ocrEngine = GetCurrentOcrEngine();
            var ocrResult = await ocrEngine.RecognizeAsync(regionImage);
            if (_selectionGeneration != generation) return;

            if (ocrResult.TextBlocks.Count == 0)
            {
                _state = OverlayState.Selecting;
                return;
            }

            _originalText = ocrResult.FullText;
            _lastOcrTextBlocks = ocrResult.TextBlocks;
            Log.Information("OCR result: {Text}", _originalText.Length > 50 ? _originalText[..50] + "..." : _originalText);

            var sourceLang = Toolbar.GetSourceLanguage();
            var targetLang = Toolbar.GetTargetLanguage();
            var translationEngine = GetCurrentTranslationEngine();
            var translationResult = await translationEngine.TranslateAsync(
                _originalText, sourceLang, targetLang);
            if (_selectionGeneration != generation) return;

            _translatedText = translationResult.TranslatedText;
            Log.Information("Translation: {Text}", _translatedText.Length > 50 ? _translatedText[..50] + "..." : _translatedText);

            var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData, selection);
            var wpfBgColor = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp(bgColor.Val2, 0, 255),
                (byte)Math.Clamp(bgColor.Val1, 0, 255),
                (byte)Math.Clamp(bgColor.Val0, 0, 255));

            var dpiScaleX = _screenshotDpiX / 96.0;
            var dpiScaleY = _screenshotDpiY / 96.0;

            // 按文字框位置逐块渲染译文
            var translatedLines = _translatedText.Split('\n');
            var blocks = ocrResult.TextBlocks;
            var translatedBlocks = new List<(string Text, Rect BoundingBox)>();

            if (translatedLines.Length == blocks.Count)
            {
                // 行数匹配：每行译文对应一个文字框
                for (int i = 0; i < blocks.Count; i++)
                    translatedBlocks.Add((translatedLines[i], blocks[i].BoundingBox));
            }
            else
            {
                // 行数不匹配：逐块翻译
                var engine = GetCurrentTranslationEngine();
                foreach (var block in blocks)
                {
                    if (string.IsNullOrWhiteSpace(block.Text)) continue;
                    var r = await engine.TranslateAsync(block.Text, sourceLang, targetLang);
                    if (_selectionGeneration != generation) return;
                    translatedBlocks.Add((r.TranslatedText, block.BoundingBox));
                }
            }

            // 估算字号（基于所有文字框的中位数高度）
            var heights = blocks
                .Where(b => b.BoundingBox.Height > 0)
                .Select(b => b.BoundingBox.Height / dpiScaleY)
                .OrderBy(h => h)
                .ToArray();
            var baseFontSize = heights.Length > 0
                ? heights[heights.Length / 2]
                : selection.Height * 0.75;

            var fontMode = _configManager.Settings.Other.FontSizeMode;
            var customSize = _configManager.Settings.Other.CustomFontSize;
            var styleInfo = _styleAnalyzer.Analyze(selection, _originalText, baseFontSize, fontMode, customSize, wpfBgColor);

            var filledImage = _imageProcessor.FillRegion(_screenshotData, selection, bgColor);

            var resultImage = _textRenderer.RenderTranslatedBlocks(
                filledImage, translatedBlocks, selection, styleInfo,
                _screenshotDpiX, _screenshotDpiY, dpiScaleX, dpiScaleY);
            Log.Debug("RenderTranslatedBlocks done: {Count} blocks, Pixel={PW}x{PH}",
                translatedBlocks.Count, resultImage.PixelWidth, resultImage.PixelHeight);

            _translationResultImage = resultImage;
            BackgroundImage.Source = resultImage;
            Log.Debug("BackgroundImage.Source set: Image.ActualSize={W}x{H}, Image.Visibility={Vis}, Grid.ActualSize={GW}x{GH}, OverlayCanvas.ActualSize={OW}x{OH}, Window.ActualSize={WW}x{WH}",
                BackgroundImage.ActualWidth, BackgroundImage.ActualHeight, BackgroundImage.Visibility,
                RootGrid.ActualWidth, RootGrid.ActualHeight,
                OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight,
                ActualWidth, ActualHeight);
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();

            Toolbar.SetData(_originalText, _translatedText);
            Toolbar.Visibility = Visibility.Visible;
            PositionToolbar(selection);

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
        if (!_autoPositionToolbar) return;
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = Toolbar.DesiredSize;

        var screenW = ActualWidth;
        var screenH = ActualHeight;

        // 优先级: 下 → 上 → 右 → 左 → 贴边
        double x, y;

        // 尝试下方
        y = selection.Bottom + 8;
        if (y + toolbarSize.Height > screenH)
        {
            // 尝试上方
            y = selection.Top - toolbarSize.Height - 8;
            if (y < 0)
            {
                // 尝试右侧
                x = selection.Right + 8;
                y = selection.Y;
                if (x + toolbarSize.Width > screenW)
                {
                    // 尝试左侧
                    x = selection.Left - toolbarSize.Width - 8;
                    if (x < 0)
                        x = 8; // 贴边
                }
                // 纵向 clamp
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

        // 横向 clamp
        x = Math.Max(8, Math.Min(x, screenW - toolbarSize.Width - 8));
        // 纵向 clamp
        y = Math.Max(8, Math.Min(y, screenH - toolbarSize.Height - 8));

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    private void HandleReselect()
    {
        _autoPositionToolbar = true;
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

    private static string MapOcrDisplayName(string engineName) => engineName switch
    {
        "PaddleOCR" => "PaddleOCR (本地)",
        "RemoteOCR" => "RemoteOCR (远程)",
        _ => engineName
    };

    private static string UnmapOcrDisplayName(string displayName) => displayName switch
    {
        "PaddleOCR (本地)" => "PaddleOCR",
        "RemoteOCR (远程)" => "RemoteOCR",
        _ => displayName
    };

    private static string MapTranslationDisplayName(string engineName) => engineName switch
    {
        "Google" => "Google",
        "DeepL" => "DeepL",
        "Baidu" => "百度",
        "OpenAI" => "OpenAI",
        _ => engineName
    };

    private static string UnmapTranslationDisplayName(string displayName) => displayName switch
    {
        "百度" => "Baidu",
        _ => displayName
    };

    private void RerunAll()
    {
        if (_state != OverlayState.Result || _screenshotData == null) return;
        _ = ProcessSelectionAsync(_currentSelection, ++_selectionGeneration);
    }

    private void RerunTranslation()
    {
        if (_state != OverlayState.Result || _screenshotData == null || string.IsNullOrEmpty(_originalText)) return;
        _ = ReTranslateAsync();
    }

    private async Task ReTranslateAsync()
    {
        var gen = _selectionGeneration;
        try
        {
            var sourceLang = Toolbar.GetSourceLanguage();
            var targetLang = Toolbar.GetTargetLanguage();
            var engine = GetCurrentTranslationEngine();
            var blocks = _lastOcrTextBlocks;
            if (blocks == null || blocks.Count == 0) return;

            // 逐块翻译
            var translatedBlocks = new List<(string Text, Rect BoundingBox)>();
            var allTexts = new List<string>();
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text)) continue;
                var r = await engine.TranslateAsync(block.Text, sourceLang, targetLang);
                if (_selectionGeneration != gen) return;
                translatedBlocks.Add((r.TranslatedText, block.BoundingBox));
                allTexts.Add(r.TranslatedText);
            }

            _translatedText = string.Join("\n", allTexts);
            Log.Information("重新翻译: {Text}", _translatedText.Length > 50 ? _translatedText[..50] + "..." : _translatedText);

            var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData!, _currentSelection);
            var wpfBgColor = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp(bgColor.Val2, 0, 255),
                (byte)Math.Clamp(bgColor.Val1, 0, 255),
                (byte)Math.Clamp(bgColor.Val0, 0, 255));

            var dpiScaleX = _screenshotDpiX / 96.0;
            var dpiScaleY = _screenshotDpiY / 96.0;

            var heights = blocks
                .Where(b => b.BoundingBox.Height > 0)
                .Select(b => b.BoundingBox.Height / dpiScaleY)
                .OrderBy(h => h)
                .ToArray();
            var baseFontSize = heights.Length > 0 ? heights[heights.Length / 2] : _currentSelection.Height * 0.75;

            var fontMode = _configManager.Settings.Other.FontSizeMode;
            var customSize = _configManager.Settings.Other.CustomFontSize;
            var styleInfo = _styleAnalyzer.Analyze(_currentSelection, _originalText, baseFontSize, fontMode, customSize, wpfBgColor);

            var filledImage = _imageProcessor.FillRegion(_screenshotData!, _currentSelection, bgColor);
            var resultImage = _textRenderer.RenderTranslatedBlocks(
                filledImage, translatedBlocks, _currentSelection, styleInfo,
                _screenshotDpiX, _screenshotDpiY, dpiScaleX, dpiScaleY);

            if (_selectionGeneration != gen) return;
            _translationResultImage = resultImage;
            BackgroundImage.Source = resultImage;
            Toolbar.SetData(_originalText, _translatedText);
        }
        catch (Exception ex)
        {
            if (_selectionGeneration != gen) return;
            Log.Error(ex, "重新翻译失败");
        }
    }

    private IOcrEngine GetCurrentOcrEngine()
    {
        if (!string.IsNullOrEmpty(_currentOcrEngineName) && _ocrEngines.TryGetValue(_currentOcrEngineName, out var e) && e.IsAvailable)
            return e;
        return _ocrEngine;
    }

    private ITranslationEngine GetCurrentTranslationEngine()
    {
        if (!string.IsNullOrEmpty(_currentTranslationEngineName) && _translationEngines.TryGetValue(_currentTranslationEngineName, out var e) && e.IsAvailable)
            return e;
        return _translationEngine;
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

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

public enum OverlayViewMode
{
    OriginalImage,
    OriginalText,
    TranslatedText
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
    private OverlayViewMode _viewMode = OverlayViewMode.OriginalText;
    private Point _selectionStart;
    private Rect _currentSelection;
    private byte[]? _screenshotData;
    private string _originalText = "";
    private string _translatedText = "";
    private int _selectionGeneration;
    private double _screenshotDpiX = 96;
    private double _screenshotDpiY = 96;
    private bool _autoPositionToolbar = true;
    private string _currentOcrEngineName = "";
    private string _currentTranslationEngineName = "";

    // 缓存：用于视图切换时无需重新 OCR/翻译
    private IReadOnlyList<Models.TextBlock>? _lastOcrTextBlocks;
    private IReadOnlyList<(string Text, Rect BoundingBox)>? _translatedBlocks;
    private byte[]? _filledImageBytes;
    private TextStyleInfo? _originalStyle;
    private TextStyleInfo? _translatedStyle;

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

        SizeChanged += (_, args) =>
        {
            Mask.Width = args.NewSize.Width;
            Mask.Height = args.NewSize.Height;
        };

        Toolbar.OnReselect += HandleReselect;
        Toolbar.OnExit += HandleExit;
        Toolbar.OnDragStarted += () => _autoPositionToolbar = false;
        Toolbar.OnViewModeChanged += mode => ApplyViewMode(mode);
        Toolbar.OnShowOriginalImage += () => ApplyViewMode(OverlayViewMode.OriginalImage);
        Toolbar.OnOriginalBgFillChanged += _ => ApplyViewMode(_viewMode);
        Toolbar.OnTranslatedBgFillChanged += _ => ApplyViewMode(_viewMode);
        Toolbar.OnLanguageChanged += (which, value) =>
        {
            if (which == "source")
                _configManager.Settings.Language.Source = value;
            else
                _configManager.Settings.Language.Target = value;
            _configManager.Save();
            RerunTranslation();
        };
        Toolbar.OnEngineChanged += (which, value) =>
        {
            if (which == "ocr")
            {
                var name = UnmapOcrDisplayName(value);
                if (_ocrEngines.ContainsKey(name))
                {
                    _currentOcrEngineName = name;
                    _configManager.Settings.Ocr.ActiveEngine = name;
                    _configManager.Save();
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
                    _configManager.Settings.Translation.ActiveEngine = name;
                    _configManager.Save();
                    Log.Information("切换翻译引擎: {Engine}", name);
                    RerunTranslation();
                }
            }
        };

        var ocrNames = _ocrEngines.Keys.ToArray();
        var transNames = _translationEngines.Keys.ToArray();
        _currentOcrEngineName = _configManager.Settings.Ocr.ActiveEngine;
        _currentTranslationEngineName = _configManager.Settings.Translation.ActiveEngine;
        Log.Information("配置加载: OCR={Ocr}, Translation={Trans}", _currentOcrEngineName, _currentTranslationEngineName);
        if (!_ocrEngines.ContainsKey(_currentOcrEngineName))
            _currentOcrEngineName = ocrNames.FirstOrDefault() ?? "";
        if (!_translationEngines.ContainsKey(_currentTranslationEngineName))
            _currentTranslationEngineName = transNames.FirstOrDefault() ?? "";

        // 先暂停事件，避免 SetEngines 触发 SelectionChanged 覆盖配置
        Toolbar.SuspendEvents();
        Toolbar.SetEngines(
            ocrNames.Select(MapOcrDisplayName).ToArray(),
            transNames.Select(MapTranslationDisplayName).ToArray());
        Toolbar.SetSelectedOcrEngine(MapOcrDisplayName(_currentOcrEngineName));
        Toolbar.SetSelectedTranslationEngine(MapTranslationDisplayName(_currentTranslationEngineName));
        Toolbar.SetSourceLanguage(_configManager.Settings.Language.Source);
        Toolbar.SetTargetLanguage(_configManager.Settings.Language.Target);
        Toolbar.ResumeEvents();
        Toolbar.SetSourceLanguage(_configManager.Settings.Language.Source);
        Toolbar.SetTargetLanguage(_configManager.Settings.Language.Target);

        // 最小化/恢复模式：避免首次 Show 时最大化动画导致白屏闪烁
        Loaded += (_, _) => WindowState = WindowState.Maximized;
        WindowState = WindowState.Minimized;
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
            BackgroundImage.Source = bitmapImage;

            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            TextOverlayCanvas.Children.Clear();
            TextOverlayCanvas.IsHitTestVisible = false;
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
            _selectionGeneration++;
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

    // ============ 核心处理流程 ============

    private async Task ProcessSelectionAsync(Rect selection, int generation)
    {
        if (_state == OverlayState.Exiting || _screenshotData == null) return;

        _state = OverlayState.Processing;

        try
        {
            // === 阶段 1：OCR + 原文覆盖 ===
            var regionImage = _screenshotService.CropRegion(_screenshotData, selection);
            if (_selectionGeneration != generation) return;

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
            Log.Information("OCR: {Count} blocks, {Text}", ocrResult.TextBlocks.Count,
                _originalText.Length > 50 ? _originalText[..50] + "..." : _originalText);

            // 计算样式
            var dpiScaleX = _screenshotDpiX / 96.0;
            var dpiScaleY = _screenshotDpiY / 96.0;
            var wpfBgColor = System.Windows.Media.Color.FromRgb(0, 0, 0); // 占位，翻译阶段再采样
            _originalStyle = ComputeStyle(ocrResult.TextBlocks, selection, dpiScaleY, wpfBgColor);

            // 显示原文覆盖
            Mask.ClearSelection();
            SelectionLayer.ClearSelection();
            TextOverlayCanvas.IsHitTestVisible = true;
            PopulateTextOverlays(ocrResult.TextBlocks, selection, _originalStyle, dpiScaleX, dpiScaleY, true);

            _viewMode = OverlayViewMode.OriginalText;
            Toolbar.SetData(_originalText, "");
            Toolbar.SetViewMode(OverlayViewMode.OriginalText);
            Toolbar.Visibility = Visibility.Visible;
            PositionToolbar(selection);
            _state = OverlayState.Result;

            // === 阶段 2：翻译 + 译文覆盖 ===
            var sourceLang = Toolbar.GetSourceLanguage();
            var targetLang = Toolbar.GetTargetLanguage();
            var translationEngine = GetCurrentTranslationEngine();

            // 尝试整段翻译，再按行匹配
            var translationResult = await translationEngine.TranslateAsync(_originalText, sourceLang, targetLang);
            if (_selectionGeneration != generation) return;

            _translatedText = translationResult.TranslatedText;
            Log.Information("翻译: {Text}", _translatedText.Length > 50 ? _translatedText[..50] + "..." : _translatedText);

            var translatedLines = _translatedText.Split('\n');
            var blocks = ocrResult.TextBlocks;
            var translatedBlocks = new List<(string Text, Rect BoundingBox)>();

            if (translatedLines.Length == blocks.Count)
            {
                for (int i = 0; i < blocks.Count; i++)
                    translatedBlocks.Add((translatedLines[i], blocks[i].BoundingBox));
            }
            else
            {
                // 行数不匹配，逐块翻译
                foreach (var block in blocks)
                {
                    if (string.IsNullOrWhiteSpace(block.Text)) continue;
                    var r = await translationEngine.TranslateAsync(block.Text, sourceLang, targetLang);
                    if (_selectionGeneration != generation) return;
                    translatedBlocks.Add((r.TranslatedText, block.BoundingBox));
                }
            }
            _translatedBlocks = translatedBlocks;

            // 采样背景色并填充
            var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData, selection);
            wpfBgColor = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp(bgColor.Val2, 0, 255),
                (byte)Math.Clamp(bgColor.Val1, 0, 255),
                (byte)Math.Clamp(bgColor.Val0, 0, 255));
            _translatedStyle = ComputeStyle(blocks, selection, dpiScaleY, wpfBgColor);

            _filledImageBytes = _imageProcessor.FillRegion(_screenshotData, selection, bgColor);
            if (_selectionGeneration != generation) return;

            // 自动切换到译文视图
            Toolbar.SetData(_originalText, _translatedText);
            ApplyViewMode(OverlayViewMode.TranslatedText);
            Toolbar.SetViewMode(OverlayViewMode.TranslatedText);
        }
        catch (Exception ex)
        {
            if (_selectionGeneration != generation) return;
            Log.Error(ex, "处理选区失败");
            _state = OverlayState.Selecting;

            Hide();
            MessageBox.Show($"处理失败: {ex.Message}\n\n请检查引擎配置（右键托盘图标 → 设置）。",
                "OverlayTranslate", MessageBoxButton.OK, MessageBoxImage.Warning);
            Show();
            Activate();
        }
    }

    // ============ 视图切换 ============

    private void ApplyViewMode(OverlayViewMode mode)
    {
        _viewMode = mode;
        TextOverlayCanvas.Children.Clear();

        var dpiScaleX = _screenshotDpiX / 96.0;
        var dpiScaleY = _screenshotDpiY / 96.0;
        var originalBgFill = Toolbar.IsOriginalBgFillEnabled;
        var translatedBgFill = Toolbar.IsTranslatedBgFillEnabled;

        switch (mode)
        {
            case OverlayViewMode.OriginalImage:
                if (_screenshotData != null)
                    BackgroundImage.Source = BytesToBitmapImage(_screenshotData);
                break;

            case OverlayViewMode.OriginalText:
                if (_screenshotData != null)
                    BackgroundImage.Source = BytesToBitmapImage(_screenshotData);
                if (_lastOcrTextBlocks != null && _originalStyle != null)
                    PopulateTextOverlays(_lastOcrTextBlocks, _currentSelection, _originalStyle, dpiScaleX, dpiScaleY, originalBgFill);
                break;

            case OverlayViewMode.TranslatedText:
                // 译文底色覆盖 = FillRegion + TextBox 背景，统一控制
                if (translatedBgFill && _filledImageBytes != null)
                    BackgroundImage.Source = BytesToBitmapImage(_filledImageBytes);
                else if (_screenshotData != null)
                    BackgroundImage.Source = BytesToBitmapImage(_screenshotData);
                if (_translatedBlocks != null && _translatedStyle != null)
                    PopulateTranslatedOverlays(_translatedBlocks, _currentSelection, _translatedStyle, dpiScaleX, dpiScaleY, translatedBgFill);
                break;
        }
    }

    // ============ TextBox 覆盖 ============

    private void PopulateTextOverlays(IReadOnlyList<Models.TextBlock> blocks, Rect selection,
        TextStyleInfo style, double dpiScaleX, double dpiScaleY, bool bgFill)
    {
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            var dipX = selection.X + block.BoundingBox.X / dpiScaleX;
            var dipY = selection.Y + block.BoundingBox.Y / dpiScaleY;
            var dipW = block.BoundingBox.Width / dpiScaleX;
            var dipH = block.BoundingBox.Height / dpiScaleY;

            var fontSize = Math.Max(8, dipH * 0.75);
            var tb = CreateSelectableTextBox(block.Text, fontSize, dipW, dipH, style.TextColor, bgFill);
            Canvas.SetLeft(tb, dipX);
            Canvas.SetTop(tb, dipY);
            TextOverlayCanvas.Children.Add(tb);
        }
    }

    private void PopulateTranslatedOverlays(IReadOnlyList<(string Text, Rect BoundingBox)> blocks,
        Rect selection, TextStyleInfo style, double dpiScaleX, double dpiScaleY, bool bgFill)
    {
        foreach (var (text, bbox) in blocks)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            var dipX = selection.X + bbox.X / dpiScaleX;
            var dipY = selection.Y + bbox.Y / dpiScaleY;
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

    private TextStyleInfo ComputeStyle(IReadOnlyList<Models.TextBlock> blocks, Rect selection,
        double dpiScaleY, Color bgColor)
    {
        var heights = blocks
            .Where(b => b.BoundingBox.Height > 0)
            .Select(b => b.BoundingBox.Height / dpiScaleY)
            .OrderBy(h => h)
            .ToArray();
        var baseFontSize = heights.Length > 0 ? heights[heights.Length / 2] : selection.Height * 0.75;

        var fontMode = _configManager.Settings.Other.FontSizeMode;
        var customSize = _configManager.Settings.Other.CustomFontSize;
        return _styleAnalyzer.Analyze(selection, _originalText, baseFontSize, fontMode, customSize, bgColor);
    }

    // ============ 重新处理 ============

    private void RerunAll()
    {
        if (_state != OverlayState.Result || _screenshotData == null) return;
        _filledImageBytes = null; // 需要重新填充
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
            _translatedBlocks = translatedBlocks;

            // 如果之前没有填充图，生成一个
            if (_filledImageBytes == null)
            {
                var bgColor = _imageProcessor.SampleBackgroundColor(_screenshotData!, _currentSelection);
                _filledImageBytes = _imageProcessor.FillRegion(_screenshotData!, _currentSelection, bgColor);
            }

            if (_selectionGeneration != gen) return;

            // 如果当前在译文视图，更新覆盖
            if (_viewMode == OverlayViewMode.TranslatedText)
            {
                var dpiScaleX = _screenshotDpiX / 96.0;
                var dpiScaleY = _screenshotDpiY / 96.0;
                if (_translatedStyle != null)
                    PopulateTranslatedOverlays(translatedBlocks, _currentSelection, _translatedStyle, dpiScaleX, dpiScaleY, Toolbar.IsTranslatedBgFillEnabled);
            }

            Toolbar.SetData(_originalText, _translatedText);
        }
        catch (Exception ex)
        {
            if (_selectionGeneration != gen) return;
            Log.Error(ex, "重新翻译失败");
        }
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
        _selectionGeneration++;
        _state = OverlayState.Selecting;
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        TextOverlayCanvas.Children.Clear();
        TextOverlayCanvas.IsHitTestVisible = false;
        Toolbar.Visibility = Visibility.Collapsed;

        if (_screenshotData != null)
            BackgroundImage.Source = BytesToBitmapImage(_screenshotData);
    }

    private void HandleExit() => ExitOverlay();

    private void ExitOverlay()
    {
        _state = OverlayState.Exiting;
        Mask.ClearSelection();
        SelectionLayer.ClearSelection();
        TextOverlayCanvas.Children.Clear();
        TextOverlayCanvas.IsHitTestVisible = false;
        Toolbar.Visibility = Visibility.Collapsed;
        BackgroundImage.Source = null;
        _screenshotData = null;
        _lastOcrTextBlocks = null;
        _translatedBlocks = null;
        _filledImageBytes = null;
        _originalStyle = null;
        _translatedStyle = null;
        Hide();
        _state = OverlayState.Idle;
    }

    // ============ 引擎辅助 ============

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

using System.Linq;
using System.Windows;
using System.Windows.Media;
using OverlayTranslate.Engines;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Models;
using Serilog;

namespace OverlayTranslate.Services;

public class PipelineResult
{
    public string OriginalText { get; init; } = "";
    public string TranslatedText { get; init; } = "";
    public IReadOnlyList<TextBlock> OcrBlocks { get; init; } = [];
    public IReadOnlyList<(string Text, Rect BoundingBox)> TranslatedBlocks { get; init; } = [];
    public byte[]? FilledImageBytes { get; init; }
    public TextStyleInfo OriginalStyle { get; init; } = new();
    public TextStyleInfo TranslatedStyle { get; init; } = new();
}

public class TranslationPipeline
{
    private readonly ScreenshotService _screenshotService;
    private readonly ImageProcessor _imageProcessor;
    private readonly StyleAnalyzer _styleAnalyzer;
    private readonly ConfigManager _configManager;
    private readonly Dictionary<string, IOcrEngine> _ocrEngines;
    private readonly Dictionary<string, ITranslationEngine> _translationEngines;
    private readonly IOcrEngine _defaultOcrEngine;
    private readonly ITranslationEngine _defaultTranslationEngine;

    public TranslationPipeline(
        ScreenshotService screenshotService,
        ImageProcessor imageProcessor,
        StyleAnalyzer styleAnalyzer,
        ConfigManager configManager,
        Dictionary<string, IOcrEngine> ocrEngines,
        Dictionary<string, ITranslationEngine> translationEngines,
        IOcrEngine defaultOcrEngine,
        ITranslationEngine defaultTranslationEngine)
    {
        _screenshotService = screenshotService;
        _imageProcessor = imageProcessor;
        _styleAnalyzer = styleAnalyzer;
        _configManager = configManager;
        _ocrEngines = ocrEngines;
        _translationEngines = translationEngines;
        _defaultOcrEngine = defaultOcrEngine;
        _defaultTranslationEngine = defaultTranslationEngine;
    }

    public IOcrEngine GetOcrEngine(string engineName)
    {
        if (!string.IsNullOrEmpty(engineName) && _ocrEngines.TryGetValue(engineName, out var e) && e.IsAvailable)
            return e;
        return _defaultOcrEngine;
    }

    public ITranslationEngine GetTranslationEngine(string engineName)
    {
        if (!string.IsNullOrEmpty(engineName) && _translationEngines.TryGetValue(engineName, out var e) && e.IsAvailable)
            return e;
        return _defaultTranslationEngine;
    }

    public string[] GetAvailableOcrEngines() => _ocrEngines.Keys.ToArray();
    public string[] GetAvailableTranslationEngines() => _translationEngines.Keys.ToArray();

    public async Task<PipelineResult?> ExecuteAsync(
        byte[] screenshotData, Rect selection,
        double screenshotDpiX, double screenshotDpiY,
        string ocrEngineName, string translationEngineName,
        string sourceLang, string targetLang,
        CancellationToken ct)
    {
        // 阶段 1：截图裁剪
        var regionImage = _screenshotService.CropRegion(screenshotData, selection);

        // 阶段 2：OCR
        var ocrEngine = GetOcrEngine(ocrEngineName);
        var ocrResult = await ocrEngine.RecognizeAsync(regionImage, ct: ct);
        ct.ThrowIfCancellationRequested();

        if (ocrResult.TextBlocks.Count == 0)
            return null;

        var originalText = ocrResult.FullText;
        Log.Information("OCR: {Count} blocks, {Text}", ocrResult.TextBlocks.Count,
            originalText.Length > 50 ? originalText[..50] + "..." : originalText);

        // 阶段 3：计算样式
        var dpiScaleY = screenshotDpiY / 96.0;
        var originalStyle = ComputeStyle(ocrResult.TextBlocks, selection, dpiScaleY);

        // 阶段 4：翻译
        var translationEngine = GetTranslationEngine(translationEngineName);
        var translationResult = await translationEngine.TranslateAsync(originalText, sourceLang, targetLang, ct);

        var translatedText = translationResult.TranslatedText;
        Log.Information("翻译: {Text}", translatedText.Length > 50 ? translatedText[..50] + "..." : translatedText);

        var translatedLines = translatedText.Split('\n');
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
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(block.Text)) continue;
                var r = await translationEngine.TranslateAsync(block.Text, sourceLang, targetLang, ct);
                translatedBlocks.Add((r.TranslatedText, block.BoundingBox));
            }
        }

        // 阶段 5：采样背景色并填充（仅填充译文块区域，而非整个选区）
        var bgColor = _imageProcessor.SampleBackgroundColor(screenshotData, selection);
        var wpfBgColor = System.Windows.Media.Color.FromRgb(
            (byte)Math.Clamp(bgColor.Val2, 0, 255),
            (byte)Math.Clamp(bgColor.Val1, 0, 255),
            (byte)Math.Clamp(bgColor.Val0, 0, 255));
        var translatedStyle = ComputeStyle(blocks, selection, dpiScaleY, wpfBgColor);

        // 将每个译文块的相对坐标转换为绝对坐标
        var blockRects = translatedBlocks
            .Select(tb => new System.Windows.Rect(
                selection.X + tb.BoundingBox.X,
                selection.Y + tb.BoundingBox.Y,
                tb.BoundingBox.Width,
                tb.BoundingBox.Height))
            .ToList();
        var filledImageBytes = _imageProcessor.FillRegions(screenshotData, blockRects, bgColor);

        return new PipelineResult
        {
            OriginalText = originalText,
            TranslatedText = translatedText,
            OcrBlocks = ocrResult.TextBlocks,
            TranslatedBlocks = translatedBlocks,
            FilledImageBytes = filledImageBytes,
            OriginalStyle = originalStyle,
            TranslatedStyle = translatedStyle
        };
    }

    public async Task<PipelineResult> TranslateBlocksAsync(
        byte[] screenshotData, Rect selection,
        IReadOnlyList<TextBlock> ocrBlocks, string originalText,
        double screenshotDpiX, double screenshotDpiY,
        string translationEngineName, string sourceLang, string targetLang,
        CancellationToken ct)
    {
        // 阶段 1：计算 DPI 比例
        var dpiScaleY = screenshotDpiY / 96.0;

        // 阶段 2：计算原文样式（placeholderBg = 黑色）
        var placeholderBg = System.Windows.Media.Color.FromRgb(0, 0, 0);
        var originalStyle = ComputeStyle(ocrBlocks, selection, dpiScaleY, placeholderBg);

        // 阶段 3：翻译
        var translationEngine = GetTranslationEngine(translationEngineName);
        var translationResult = await translationEngine.TranslateAsync(originalText, sourceLang, targetLang, ct);
        ct.ThrowIfCancellationRequested();

        var translatedText = translationResult.TranslatedText;
        Log.Information("翻译: {Text}", translatedText.Length > 50 ? translatedText[..50] + "..." : translatedText);

        // 阶段 4：行匹配或逐块翻译
        var translatedLines = translatedText.Split('\n');
        var blocks = ocrBlocks;
        var translatedBlocks = new List<(string Text, Rect BoundingBox)>();

        if (translatedLines.Length == blocks.Count)
        {
            for (int i = 0; i < blocks.Count; i++)
                translatedBlocks.Add((translatedLines[i], blocks[i].BoundingBox));
        }
        else
        {
            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(block.Text)) continue;
                var r = await translationEngine.TranslateAsync(block.Text, sourceLang, targetLang, ct);
                translatedBlocks.Add((r.TranslatedText, block.BoundingBox));
            }
        }

        // 阶段 5：采样背景色，计算译文样式
        var bgColor = _imageProcessor.SampleBackgroundColor(screenshotData, selection);
        var wpfBgColor = System.Windows.Media.Color.FromRgb(
            (byte)Math.Clamp(bgColor.Val2, 0, 255),
            (byte)Math.Clamp(bgColor.Val1, 0, 255),
            (byte)Math.Clamp(bgColor.Val0, 0, 255));
        var translatedStyle = ComputeStyle(blocks, selection, dpiScaleY, wpfBgColor);

        // 阶段 6：FillRegion（仅填充译文块区域，而非整个选区）
        var blockRects = translatedBlocks
            .Select(tb => new System.Windows.Rect(
                selection.X + tb.BoundingBox.X,
                selection.Y + tb.BoundingBox.Y,
                tb.BoundingBox.Width,
                tb.BoundingBox.Height))
            .ToList();
        var filledImageBytes = _imageProcessor.FillRegions(screenshotData, blockRects, bgColor);

        return new PipelineResult
        {
            OriginalText = originalText,
            TranslatedText = translatedText,
            OcrBlocks = ocrBlocks,
            TranslatedBlocks = translatedBlocks,
            FilledImageBytes = filledImageBytes,
            OriginalStyle = originalStyle,
            TranslatedStyle = translatedStyle
        };
    }

    public async Task<PipelineResult> ReTranslateAsync(
        byte[] screenshotData, Rect selection,
        IReadOnlyList<TextBlock> ocrBlocks,
        string sourceLang, string targetLang,
        string translationEngineName,
        CancellationToken ct)
    {
        var engine = GetTranslationEngine(translationEngineName);
        var translatedBlocks = new List<(string Text, Rect BoundingBox)>();
        var allTexts = new List<string>();

        foreach (var block in ocrBlocks)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(block.Text)) continue;
            var r = await engine.TranslateAsync(block.Text, sourceLang, targetLang, ct);
            translatedBlocks.Add((r.TranslatedText, block.BoundingBox));
            allTexts.Add(r.TranslatedText);
        }

        var translatedText = string.Join("\n", allTexts);

        // 采样背景色并生成填充图（仅填充译文块区域，而非整个选区）
        var bgColor = _imageProcessor.SampleBackgroundColor(screenshotData, selection);
        var blockRects = translatedBlocks
            .Select(tb => new System.Windows.Rect(
                selection.X + tb.BoundingBox.X,
                selection.Y + tb.BoundingBox.Y,
                tb.BoundingBox.Width,
                tb.BoundingBox.Height))
            .ToList();
        var filledImageBytes = _imageProcessor.FillRegions(screenshotData, blockRects, bgColor);

        return new PipelineResult
        {
            TranslatedText = translatedText,
            OcrBlocks = ocrBlocks,
            TranslatedBlocks = translatedBlocks,
            FilledImageBytes = filledImageBytes
        };
    }

    private TextStyleInfo ComputeStyle(IReadOnlyList<TextBlock> blocks, Rect selection,
        double dpiScaleY, Color? bgColor = null)
    {
        var heights = blocks
            .Where(b => b.BoundingBox.Height > 0)
            .Select(b => b.BoundingBox.Height / dpiScaleY)
            .OrderBy(h => h)
            .ToArray();
        var baseFontSize = heights.Length > 0 ? heights[heights.Length / 2] : selection.Height * 0.75;

        var fontMode = _configManager.Settings.Other.FontSizeMode;
        var customSize = _configManager.Settings.Other.CustomFontSize;

        // 合并所有文本用于样式分析
        var allText = string.Join("\n", blocks.Select(b => b.Text));

        var placeholderBg = bgColor ?? System.Windows.Media.Color.FromRgb(0, 0, 0);
        return _styleAnalyzer.Analyze(selection, allText, baseFontSize, fontMode, customSize, placeholderBg);
    }
}

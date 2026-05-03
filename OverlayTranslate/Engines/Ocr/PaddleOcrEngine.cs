using System.IO;
using System.Threading;
using System.Windows;
using OverlayTranslate.Models;
using PaddleOCRSharp;
using Serilog;

namespace OverlayTranslate.Engines.Ocr;

public class PaddleOcrEngine : IOcrEngine
{
    public string Name => "PaddleOCR";
    public bool IsAvailable => _engine != null;

    private PaddleOCREngine? _engine;
    private readonly string _modelPath;

    public PaddleOcrEngine(string modelPath = "inference/")
    {
        _modelPath = modelPath;
        try
        {
            OCRModelConfig config;
            if (_modelPath == "inference/")
            {
                // 使用默认配置（SDK 自动定位内置模型）
                config = OCRModelConfig.Default;
            }
            else
            {
                config = new OCRModelConfig(
                    det_infer_path: Path.Combine(_modelPath, "det"),
                    cls_infer_path: Path.Combine(_modelPath, "cls"),
                    rec_infer_path: Path.Combine(_modelPath, "rec"),
                    dict: Path.Combine(_modelPath, "ppocr_keys.txt"));
            }
            _engine = new PaddleOCREngine(config);
            Log.Information("PaddleOCR 引擎初始化成功，模型路径: {ModelPath}", _modelPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PaddleOCR 引擎初始化失败");
            _engine = null;
        }
    }

    public async Task<OcrResult> RecognizeAsync(byte[] imageData, string language = "auto", CancellationToken ct = default)
    {
        if (_engine == null)
            throw new InvalidOperationException("PaddleOCR 引擎未初始化");

        return await Task.Run(() =>
        {
            var result = _engine.DetectText(imageData);
            var textBlocks = new List<Models.TextBlock>();

            foreach (var block in result.TextBlocks)
            {
                // 从四个顶点计算边界矩形
                var rect = CalculateBoundingBox(block.BoxPoints);
                textBlocks.Add(new Models.TextBlock
                {
                    Text = block.Text,
                    BoundingBox = rect,
                    Confidence = block.Score,
                    Angle = 0
                });
            }

            return new OcrResult
            {
                TextBlocks = textBlocks,
                FullText = string.Join("\n", textBlocks.Select(b => b.Text)),
                Language = language
            };
        });
    }

    public string[] GetSupportedLanguages() =>
        ["ch", "en", "japan", "korean", "fr", "german", "auto"];

    /// <summary>
    /// 从四个顶点坐标计算最小外接矩形
    /// </summary>
    private static Rect CalculateBoundingBox(List<OCRPoint> points)
    {
        if (points == null || points.Count == 0)
            return Rect.Empty;

        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}

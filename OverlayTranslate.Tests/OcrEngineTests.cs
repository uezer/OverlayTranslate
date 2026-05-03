using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverlayTranslate.Engines.Ocr;
using OpenCvSharp;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;

namespace OverlayTranslate.Tests;

public class OcrEngineTests
{
    /// <summary>
    /// 用 WPF 渲染一张带中文文字的 PNG 图片。
    /// </summary>
    private static byte[] CreateTextImage(string text, int width = 400, int height = 100)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var typeface = new Typeface("Microsoft YaHei");
            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                32,
                Brushes.Black,
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            dc.DrawText(formattedText, new Point(10, 20));
        }

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// 用 OpenCvSharp 渲染一张纯色图（无文字），作为对比。
    /// </summary>
    private static byte[] CreateBlankImage(int width = 200, int height = 50)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.ImEncode(".png", mat, out var buf);
        return buf.ToArray();
    }

    [Fact]
    public void PaddleOcrEngine_InitializesSuccessfully()
    {
        var engine = new PaddleOcrEngine();
        Assert.True(engine.IsAvailable);
        Assert.Equal("PaddleOCR", engine.Name);
    }

    [Fact]
    public void PaddleOcrEngine_GetSupportedLanguages_ContainsExpected()
    {
        var engine = new PaddleOcrEngine();
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("ch", languages);
        Assert.Contains("en", languages);
        Assert.Contains("auto", languages);
    }

    [Fact]
    public async Task PaddleOcrEngine_RecognizeAsync_ChineseText()
    {
        var engine = new PaddleOcrEngine();
        if (!engine.IsAvailable)
        {
            // 跳过：PaddleOCR 环境不可用
            return;
        }

        var imageBytes = CreateTextImage("你好世界");

        var result = await engine.RecognizeAsync(imageBytes, "ch");

        Assert.NotNull(result);
        Assert.NotEmpty(result.TextBlocks);
        Assert.Contains("你好", result.FullText);
    }

    [Fact]
    public async Task PaddleOcrEngine_RecognizeAsync_EnglishText()
    {
        var engine = new PaddleOcrEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        var imageBytes = CreateTextImage("Hello World");

        var result = await engine.RecognizeAsync(imageBytes, "en");

        Assert.NotNull(result);
        Assert.NotEmpty(result.TextBlocks);
        Assert.Contains("Hello", result.FullText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PaddleOcrEngine_RecognizeAsync_BlankImage_NoText()
    {
        var engine = new PaddleOcrEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        var imageBytes = CreateBlankImage();

        var result = await engine.RecognizeAsync(imageBytes);

        Assert.NotNull(result);
        // 纯白图片应该识别不到文字或文字为空
        Assert.True(result.TextBlocks.Count == 0 || string.IsNullOrWhiteSpace(result.FullText));
    }

    [Fact]
    public async Task PaddleOcrEngine_RecognizeAsync_MixedText()
    {
        var engine = new PaddleOcrEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        var imageBytes = CreateTextImage("测试Test123");

        var result = await engine.RecognizeAsync(imageBytes, "ch");

        Assert.NotNull(result);
        Assert.NotEmpty(result.TextBlocks);
        // 应该能识别到部分文字
        Assert.False(string.IsNullOrWhiteSpace(result.FullText));
    }

    [Fact]
    public async Task PaddleOcrEngine_RecognizeAsync_BoundingBoxIsValid()
    {
        var engine = new PaddleOcrEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        var imageBytes = CreateTextImage("位置测试");

        var result = await engine.RecognizeAsync(imageBytes, "ch");

        Assert.NotEmpty(result.TextBlocks);
        foreach (var block in result.TextBlocks)
        {
            Assert.True(block.BoundingBox.Width > 0, "BoundingBox width should be > 0");
            Assert.True(block.BoundingBox.Height > 0, "BoundingBox height should be > 0");
            Assert.True(block.Confidence > 0, "Confidence should be > 0");
        }
    }

    [Fact]
    public void RemoteOcrEngine_IsNotAvailable_WhenEndpointEmpty()
    {
        var engine = new RemoteOcrEngine(new HttpClient(), "");
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void RemoteOcrEngine_IsAvailable_WhenEndpointSet()
    {
        var engine = new RemoteOcrEngine(new HttpClient(), "http://localhost:8080/ocr");
        Assert.True(engine.IsAvailable);
    }

    [Fact]
    public void RemoteOcrEngine_GetSupportedLanguages_ReturnsAuto()
    {
        var engine = new RemoteOcrEngine(new HttpClient(), "http://localhost:8080/ocr");
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("auto", languages);
    }
}

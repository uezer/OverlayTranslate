using System.Windows.Media.Imaging;
using OpenCvSharp;
using OverlayTranslate.Services;

namespace OverlayTranslate.Tests;

public class ImageProcessorTests
{
    private readonly ImageProcessor _processor = new();

    private static byte[] CreateTestImage(int width = 100, int height = 100, byte r = 255, byte g = 255, byte b = 255)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(b, g, r));
        Cv2.ImEncode(".png", mat, out var buf);
        return buf.ToArray();
    }

    /// <summary>
    /// 诊断测试：验证 FillRegion + RenderTranslatedText 不会产生全黑图像。
    /// 模拟真实流程：白色截图 → 采样背景色 → 填充选区 → 渲染译文。
    /// </summary>
    [Fact]
    public void Diagnostic_FillAndRender_ProducesNonBlackImage()
    {
        // 创建一个模拟截图：1920x1080 白色图像
        var screenshot = CreateTestImage(1920, 1080, 255, 255, 255);
        var region = new System.Windows.Rect(100, 100, 200, 50);

        // 采样背景色
        var bgColor = _processor.SampleBackgroundColor(screenshot, region);
        Assert.InRange(bgColor.Val0, 240, 255); // 应该是白色 (BGR)
        Assert.InRange(bgColor.Val1, 240, 255);
        Assert.InRange(bgColor.Val2, 240, 255);

        // 填充区域
        var filled = _processor.FillRegion(screenshot, region, bgColor);
        Assert.NotEmpty(filled);

        // 验证填充后的图像不是全黑
        using var filledMat = Cv2.ImDecode(filled, ImreadModes.Color);
        Assert.False(filledMat.Empty(), "Filled image should not be empty");
        var centerPixel = filledMat.At<Vec3b>(125, 200); // 选区中心
        Assert.True(centerPixel.Item0 > 200, $"Expected bright pixel, got B={centerPixel.Item0}");
        Assert.True(centerPixel.Item1 > 200, $"Expected bright pixel, got G={centerPixel.Item1}");
        Assert.True(centerPixel.Item2 > 200, $"Expected bright pixel, got R={centerPixel.Item2}");

        // 渲染译文
        var renderer = new TextRenderer();
        var style = new TextStyleInfo { FontSize = 16, TextColor = System.Windows.Media.Colors.Black, RegionWidth = 200, RegionHeight = 50 };
        var result = renderer.RenderTranslatedText(filled, "Hello World", region, style);

        Assert.NotNull(result);
        Assert.True(result.PixelWidth > 0, $"PixelWidth should be > 0, got {result.PixelWidth}");
        Assert.True(result.PixelHeight > 0, $"PixelHeight should be > 0, got {result.PixelHeight}");

        // 将结果编码为 PNG 并检查非黑色像素
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        var resultPng = ms.ToArray();
        Assert.NotEmpty(resultPng);

        // 用 OpenCV 解码结果 PNG 并检查
        using var resultMat = Cv2.ImDecode(resultPng, ImreadModes.Color);
        Assert.False(resultMat.Empty(), "Result PNG should decode to valid Mat");

        // 检查选区外的像素（应该保持白色）
        var outsidePixel = resultMat.At<Vec3b>(50, 50);
        Assert.True(outsidePixel.Item0 > 200, $"Outside pixel should be bright, B={outsidePixel.Item0}");

        // 检查选区内的像素（应该不是纯黑）
        var insidePixel = resultMat.At<Vec3b>(125, 200);
        var brightness = (insidePixel.Item0 + insidePixel.Item1 + insidePixel.Item2) / 3.0;
        Assert.True(brightness > 100, $"Selection area should not be black, brightness={brightness}, pixel=({insidePixel.Item0},{insidePixel.Item1},{insidePixel.Item2})");
    }

    /// <summary>
    /// 诊断测试：模拟高 DPI 截图场景。
    /// 150% DPI 屏幕：物理像素 2880x1620，逻辑坐标 1920x1080。
    /// </summary>
    [Fact]
    public void Diagnostic_HighDpi_SimulatesRealScenario()
    {
        // 模拟 150% DPI 截图：2880x1620 白色图像
        var screenshot = CreateTestImage(2880, 1620, 255, 255, 255);

        // 选区用 WPF 逻辑坐标（DIP）：1920x1080 屏幕上的选区
        var region = new System.Windows.Rect(200, 200, 300, 80);

        // 采样背景色
        var bgColor = _processor.SampleBackgroundColor(screenshot, region);
        Assert.InRange(bgColor.Val0, 240, 255);

        // 填充区域
        var filled = _processor.FillRegion(screenshot, region, bgColor);
        Assert.NotEmpty(filled);

        // 验证填充后的图像尺寸
        using var filledMat = Cv2.ImDecode(filled, ImreadModes.Color);
        Assert.Equal(2880, filledMat.Width);
        Assert.Equal(1620, filledMat.Height);

        // 渲染译文
        var renderer = new TextRenderer();
        var style = new TextStyleInfo { FontSize = 20, TextColor = System.Windows.Media.Colors.Black, RegionWidth = 300, RegionHeight = 80 };
        var result = renderer.RenderTranslatedText(filled, "测试译文 Test", region, style);

        // 验证输出尺寸匹配输入
        Assert.Equal(2880, result.PixelWidth);
        Assert.Equal(1620, result.PixelHeight);

        // 将结果编码并检查选区内不为黑色
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);

        using var resultMat = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
        // 选区中心在物理像素坐标 = DIP * 1.5
        var px = (int)((200 + 150) * 2880.0 / 1920.0);
        var py = (int)((200 + 40) * 1620.0 / 1080.0);
        var insidePixel = resultMat.At<Vec3b>(py, px);
        var brightness = (insidePixel.Item0 + insidePixel.Item1 + insidePixel.Item2) / 3.0;
        Assert.True(brightness > 100, $"High DPI selection should not be black, brightness={brightness}");
    }

    /// <summary>
    /// 诊断测试：检查 OpenCV PNG 编码是否带有 alpha 通道。
    /// 如果 PNG 有 alpha=0，WPF BitmapImage 会渲染为透明（在黑色背景上看起来是黑色）。
    /// </summary>
    [Fact]
    public void Diagnostic_CheckPngAlphaChannel()
    {
        // 创建白色图像并编码为 PNG
        using var mat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.ImEncode(".png", mat, out var buf);
        var pngBytes = buf.ToArray();

        // 用 System.Drawing 检查 PNG 是否有 alpha 通道
        using var stream = new System.IO.MemoryStream(pngBytes);
        using var image = System.Drawing.Image.FromStream(stream);
        var hasAlpha = System.Drawing.Image.IsAlphaPixelFormat(image.PixelFormat);
        var pixelFormat = image.PixelFormat;

        // 用 WPF 检查
        var wpfImage = new BitmapImage();
        using (var ms = new System.IO.MemoryStream(pngBytes))
        {
            wpfImage.BeginInit();
            wpfImage.CacheOption = BitmapCacheOption.OnLoad;
            wpfImage.StreamSource = ms;
            wpfImage.EndInit();
            wpfImage.Freeze();
        }

        // 输出信息（调试用）
        Assert.True(wpfImage.PixelWidth > 0, "WPF image should have valid pixel width");
        Assert.True(wpfImage.PixelHeight > 0, "WPF image should have valid pixel height");

        // 如果有 alpha 通道，这是潜在问题
        if (hasAlpha)
        {
            // 将 WPF 图像渲染为 RenderTargetBitmap 并检查像素
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(wpfImage, new System.Windows.Rect(0, 0, wpfImage.Width, wpfImage.Height));
            }
            var rtb = new RenderTargetBitmap(
                wpfImage.PixelWidth, wpfImage.PixelHeight,
                wpfImage.DpiX, wpfImage.DpiY,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);

            // 检查中心像素的 alpha
            var pixels = new byte[4];
            rtb.CopyPixels(new System.Windows.Int32Rect(50, 50, 1, 1), pixels, 4, 0);
            Assert.True(pixels[3] > 0, $"Alpha channel should not be 0, got A={pixels[3]}, pixel=({pixels[0]},{pixels[1]},{pixels[2]},{pixels[3]})");
        }
    }

    [Fact]
    public void SampleBackgroundColor_ReturnsColorFromRegion()
    {
        // Create a pure red image
        var imageData = CreateTestImage(100, 100, 255, 0, 0);
        var region = new System.Windows.Rect(10, 10, 20, 20);

        var color = _processor.SampleBackgroundColor(imageData, region);

        // Red channel (index 2 in BGR) should be ~255, Green and Blue ~0
        Assert.InRange(color.Val2, 240, 255); // R
        Assert.InRange(color.Val1, 0, 15);    // G
        Assert.InRange(color.Val0, 0, 15);    // B
    }

    [Fact]
    public void SampleBackgroundColor_WithMargin_SamplesAroundRegion()
    {
        // Create image: left half blue, right half red
        using var mat = new Mat(100, 100, MatType.CV_8UC3);
        // Left half blue
        Cv2.Rectangle(mat, new OpenCvSharp.Rect(0, 0, 50, 100), new Scalar(255, 0, 0), -1);
        // Right half red
        Cv2.Rectangle(mat, new OpenCvSharp.Rect(50, 0, 50, 100), new Scalar(0, 0, 255), -1);
        Cv2.ImEncode(".png", mat, out var buf);
        var imageData = buf.ToArray();

        // Sample at the boundary - margin should pick up mixed colors
        var region = new System.Windows.Rect(48, 10, 4, 10);
        var color = _processor.SampleBackgroundColor(imageData, region, sampleMargin: 5);

        // Should get a mix of blue and red
        Assert.True(color.Val0 > 10 || color.Val2 > 10); // At least one channel should be significant
    }

    [Fact]
    public void FillRegion_FillsAreaWithColor()
    {
        // Create a white image
        var imageData = CreateTestImage(100, 100, 255, 255, 255);
        var region = new System.Windows.Rect(20, 20, 30, 30);
        var fillColor = new Scalar(0, 0, 255); // Red in BGR

        var result = _processor.FillRegion(imageData, region, fillColor);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        // Decode result and check the filled area
        using var resultMat = Cv2.ImDecode(result, ImreadModes.Color);
        var pixel = resultMat.At<Vec3b>(35, 35); // Center of filled area
        Assert.InRange(pixel.Item2, 240, 255); // R
        Assert.InRange(pixel.Item1, 0, 15);    // G
        Assert.InRange(pixel.Item0, 0, 15);    // B
    }

    [Fact]
    public void FillRegion_PreservesOutsideArea()
    {
        // Create a blue image
        var imageData = CreateTestImage(100, 100, 0, 0, 255);
        var region = new System.Windows.Rect(20, 20, 30, 30);
        var fillColor = new Scalar(0, 255, 0); // Green in BGR

        var result = _processor.FillRegion(imageData, region, fillColor);

        using var resultMat = Cv2.ImDecode(result, ImreadModes.Color);
        // Check pixel outside filled area - should still be blue
        var outsidePixel = resultMat.At<Vec3b>(5, 5);
        Assert.InRange(outsidePixel.Item0, 240, 255); // B
        Assert.InRange(outsidePixel.Item1, 0, 15);    // G
        Assert.InRange(outsidePixel.Item2, 0, 15);    // R
    }

    [Fact]
    public void InpaintRegion_FillsArea()
    {
        // Create a white image with a black square
        using var mat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(mat, new OpenCvSharp.Rect(30, 30, 40, 40), new Scalar(0, 0, 0), -1);
        Cv2.ImEncode(".png", mat, out var buf);
        var imageData = buf.ToArray();

        var region = new System.Windows.Rect(30, 30, 40, 40);

        var result = _processor.InpaintRegion(imageData, region);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        // The inpainted area should no longer be pure black
        using var resultMat = Cv2.ImDecode(result, ImreadModes.Color);
        var centerPixel = resultMat.At<Vec3b>(50, 50);
        // After inpainting with Telea, the center should be closer to white than black
        var brightness = (centerPixel.Item0 + centerPixel.Item1 + centerPixel.Item2) / 3.0;
        Assert.True(brightness > 100, $"Expected brightness > 100 after inpainting, got {brightness}");
    }

    [Fact]
    public void InpaintRegion_PreservesOutsideArea()
    {
        // Create a green image
        using var mat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 255, 0));
        // Add a red rectangle to inpaint
        Cv2.Rectangle(mat, new OpenCvSharp.Rect(30, 30, 20, 20), new Scalar(0, 0, 255), -1);
        Cv2.ImEncode(".png", mat, out var buf);
        var imageData = buf.ToArray();

        var region = new System.Windows.Rect(30, 30, 20, 20);

        var result = _processor.InpaintRegion(imageData, region);

        using var resultMat = Cv2.ImDecode(result, ImreadModes.Color);
        // Pixel far from inpainted area should still be green
        var farPixel = resultMat.At<Vec3b>(5, 5);
        Assert.InRange(farPixel.Item1, 240, 255); // G
        Assert.InRange(farPixel.Item0, 0, 15);    // B
        Assert.InRange(farPixel.Item2, 0, 15);    // R
    }
}

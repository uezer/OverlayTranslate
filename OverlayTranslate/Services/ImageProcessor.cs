using System.IO;
using System.Windows;
using OpenCvSharp;

namespace OverlayTranslate.Services;

public class ImageProcessor
{
    /// <summary>
    /// 将 WPF 逻辑坐标转换为图像物理像素坐标。
    /// 通过图像物理像素尺寸与屏幕逻辑尺寸的比值计算缩放。
    /// </summary>
    private static OpenCvSharp.Rect ToPixelRect(Mat image, System.Windows.Rect region)
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;

        // 高 DPI 截图：图像物理像素 > 屏幕逻辑像素 → 需要缩放
        // 96 DPI 或测试图像：图像尺寸 ≈ 屏幕逻辑尺寸（或更小）→ 1:1 映射
        double scaleX = image.Width > screenW + 1 ? image.Width / screenW : 1.0;
        double scaleY = image.Height > screenH + 1 ? image.Height / screenH : 1.0;

        var x = Math.Max(0, (int)(region.X * scaleX));
        var y = Math.Max(0, (int)(region.Y * scaleY));
        var w = Math.Min(image.Width - x, (int)(region.Width * scaleX));
        var h = Math.Min(image.Height - y, (int)(region.Height * scaleY));

        return new OpenCvSharp.Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    public Scalar SampleBackgroundColor(byte[] imageData, System.Windows.Rect textRegion, int sampleMargin = 5)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (src.Empty()) throw new ArgumentException("Invalid image data");

        var rect = ToPixelRect(src, textRegion);

        var x = Math.Max(0, rect.X - sampleMargin);
        var y = Math.Max(0, rect.Y - sampleMargin);
        var w = Math.Min(src.Width - x, rect.Width + sampleMargin * 2);
        var h = Math.Min(src.Height - y, rect.Height + sampleMargin * 2);

        using var border = src[new OpenCvSharp.Rect(x, y, w, h)];
        var mean = Cv2.Mean(border);
        Serilog.Log.Debug("SampleBackgroundColor: img={W}x{H}, region={RX},{RY},{RW},{RH}, pixelRect={PX},{PY},{PW},{PH}, sampleRect={SX},{SY},{SW},{SH}, mean={B},{G},{R},{A}",
            src.Width, src.Height, rect.X, rect.Y, rect.Width, rect.Height, rect.X, rect.Y, rect.Width, rect.Height,
            x, y, w, h, mean.Val0, mean.Val1, mean.Val2, mean.Val3);
        return mean;
    }

    public byte[] FillRegion(byte[] imageData, System.Windows.Rect region, Scalar color)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (src.Empty()) throw new ArgumentException("Invalid image data");

        var rect = ToPixelRect(src, region);
        Serilog.Log.Debug("FillRegion: img={W}x{H}, rect={X},{Y},{W},{H}, color={B},{G},{R},{A}",
            src.Width, src.Height, rect.X, rect.Y, rect.Width, rect.Height,
            color.Val0, color.Val1, color.Val2, color.Val3);
        Cv2.Rectangle(src, rect, color, -1);

        Cv2.ImEncode(".png", src, out var buf);
        Serilog.Log.Debug("FillRegion: output PNG {Size} bytes", buf.Length);
        return buf.ToArray();
    }

    /// <summary>
    /// 填充多个区域（仅对译文块区域进行底色重绘，而非整个选区）
    /// </summary>
    public byte[] FillRegions(byte[] imageData, IReadOnlyList<System.Windows.Rect> regions, Scalar color)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (src.Empty()) throw new ArgumentException("Invalid image data");

        foreach (var region in regions)
        {
            var rect = ToPixelRect(src, region);
            if (rect.Width > 0 && rect.Height > 0)
            {
                Cv2.Rectangle(src, rect, color, -1);
            }
        }

        Cv2.ImEncode(".png", src, out var buf);
        Serilog.Log.Debug("FillRegions: filled {Count} regions, output PNG {Size} bytes", regions.Count, buf.Length);
        return buf.ToArray();
    }

    public byte[] InpaintRegion(byte[] imageData, System.Windows.Rect region)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (src.Empty()) throw new ArgumentException("Invalid image data");

        using var mask = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        using var dst = new Mat();

        var rect = ToPixelRect(src, region);
        Cv2.Rectangle(mask, rect, Scalar.All(255), -1);
        Cv2.Inpaint(src, mask, dst, 3, InpaintTypes.Telea);

        Cv2.ImEncode(".png", dst, out var buf);
        return buf.ToArray();
    }
}

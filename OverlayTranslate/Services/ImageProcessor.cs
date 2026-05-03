using System.IO;
using System.Windows;
using OpenCvSharp;

namespace OverlayTranslate.Services;

public class ImageProcessor
{
    public Scalar SampleBackgroundColor(byte[] imageData, System.Windows.Rect textRegion, int sampleMargin = 5)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var x = Math.Max(0, (int)textRegion.X - sampleMargin);
        var y = Math.Max(0, (int)textRegion.Y - sampleMargin);
        var w = Math.Min(src.Width - x, (int)textRegion.Width + sampleMargin * 2);
        var h = Math.Min(src.Height - y, (int)textRegion.Height + sampleMargin * 2);

        using var border = src[new OpenCvSharp.Rect(x, y, w, h)];
        return Cv2.Mean(border);
    }

    public byte[] FillRegion(byte[] imageData, System.Windows.Rect region, Scalar color)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var rect = new OpenCvSharp.Rect(
            Math.Max(0, (int)region.X),
            Math.Max(0, (int)region.Y),
            Math.Min(src.Width - (int)region.X, (int)region.Width),
            Math.Min(src.Height - (int)region.Y, (int)region.Height));

        Cv2.Rectangle(src, rect, color, -1);

        Cv2.ImEncode(".png", src, out var buf);
        return buf.ToArray();
    }

    public byte[] InpaintRegion(byte[] imageData, System.Windows.Rect region)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        using var mask = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        using var dst = new Mat();

        var rect = new OpenCvSharp.Rect(
            Math.Max(0, (int)region.X),
            Math.Max(0, (int)region.Y),
            Math.Min(src.Width - (int)region.X, (int)region.Width),
            Math.Min(src.Height - (int)region.Y, (int)region.Height));

        Cv2.Rectangle(mask, rect, Scalar.All(255), -1);
        Cv2.Inpaint(src, mask, dst, 3, InpaintTypes.Telea);

        Cv2.ImEncode(".png", dst, out var buf);
        return buf.ToArray();
    }
}

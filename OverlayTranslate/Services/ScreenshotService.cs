using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace OverlayTranslate.Services;

public class ScreenshotService
{
    public byte[] CaptureFullScreen()
    {
        var bounds = SystemParameters.WorkArea;
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, new System.Drawing.Size(width, height));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public byte[] CaptureRegion(Rect region)
    {
        var width = (int)region.Width;
        var height = (int)region.Height;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)region.Left, (int)region.Top, 0, 0, new System.Drawing.Size(width, height));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

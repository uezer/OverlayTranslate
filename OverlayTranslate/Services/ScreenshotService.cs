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

    public byte[] CropRegion(byte[] fullScreenshot, Rect region)
    {
        using var stream = new MemoryStream(fullScreenshot);
        using var original = System.Drawing.Image.FromStream(stream);
        using var cropped = new Bitmap((int)region.Width, (int)region.Height);
        using var graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(original,
            new Rectangle(0, 0, (int)region.Width, (int)region.Height),
            new Rectangle((int)region.X, (int)region.Y, (int)region.Width, (int)region.Height),
            GraphicsUnit.Pixel);
        using var outputStream = new MemoryStream();
        cropped.Save(outputStream, ImageFormat.Png);
        return outputStream.ToArray();
    }
}

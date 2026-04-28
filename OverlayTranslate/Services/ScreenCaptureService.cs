using System.Drawing;
using System.Drawing.Imaging;
using Screen = System.Windows.Forms.Screen;

namespace OverlayTranslate.Services;

public sealed class ScreenCaptureService : IScreenCaptureService
{
    public Bitmap CapturePrimaryScreen()
    {
        Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary screen is not available.");
        Bitmap bitmap = new(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }

    public Bitmap Crop(Bitmap source, Rectangle region)
    {
        Rectangle safeRegion = Rectangle.Intersect(new Rectangle(Point.Empty, source.Size), region);
        return source.Clone(safeRegion, PixelFormat.Format32bppArgb);
    }
}

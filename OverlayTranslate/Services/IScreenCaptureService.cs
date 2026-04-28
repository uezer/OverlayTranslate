using System.Drawing;

namespace OverlayTranslate.Services;

public interface IScreenCaptureService
{
    Bitmap CapturePrimaryScreen();

    Bitmap Crop(Bitmap source, Rectangle region);
}

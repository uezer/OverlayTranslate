using System.Drawing;
using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public interface IOcrEngine
{
    Task<IReadOnlyList<OcrBlock>> RecognizeAsync(Bitmap bitmap, SourceLanguage sourceLanguage, CancellationToken cancellationToken);
}

using System.Drawing;
using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public interface IInPlaceRenderer
{
    Bitmap Render(Bitmap original, Rectangle selection, IReadOnlyList<TranslationResult> translations);
}

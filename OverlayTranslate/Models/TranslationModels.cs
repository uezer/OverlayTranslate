using Brush = System.Windows.Media.Brush;
using FontWeight = System.Windows.FontWeight;
using Rect = System.Windows.Rect;
using TextAlignment = System.Windows.TextAlignment;

namespace OverlayTranslate.Models;

public sealed record TranslationSegment(int Index, Rect Bounds, string SourceText);

public sealed record TranslationResult(int Index, string SourceText, string TranslatedText, Rect Bounds);

public sealed record RenderStyleEstimate(
    Brush Foreground,
    double FontSize,
    FontWeight FontWeight,
    TextAlignment TextAlignment,
    string FontFamilyName,
    double LineHeight);

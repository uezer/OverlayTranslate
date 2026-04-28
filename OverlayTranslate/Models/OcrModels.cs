using Rect = System.Windows.Rect;
using Point = System.Windows.Point;

namespace OverlayTranslate.Models;

public sealed record OcrLine(
    Rect Bounds,
    IReadOnlyList<Point> Polygon,
    string Text,
    float Confidence);

public sealed record OcrBlock(
    Rect Bounds,
    IReadOnlyList<OcrLine> Lines,
    string Text);

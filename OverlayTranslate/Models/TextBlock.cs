using System.Windows;

namespace OverlayTranslate.Models;

public class TextBlock
{
    public string Text { get; set; } = "";
    public Rect BoundingBox { get; set; }
    public float Confidence { get; set; }
    public float Angle { get; set; }
}

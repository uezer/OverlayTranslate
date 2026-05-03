using System.Windows;
using System.Windows.Media;

namespace OverlayTranslate.Services;

public class TextStyleInfo
{
    public double FontSize { get; set; }
    public Color TextColor { get; set; }
    public bool IsBold { get; set; }
    public double RegionWidth { get; set; }
    public double RegionHeight { get; set; }
}

public class StyleAnalyzer
{
    public TextStyleInfo Analyze(Rect boundingBox, string text)
    {
        var estimatedFontSize = boundingBox.Height * 0.75;

        return new TextStyleInfo
        {
            FontSize = Math.Max(8, Math.Min(72, estimatedFontSize)),
            TextColor = Colors.Black,
            IsBold = false,
            RegionWidth = boundingBox.Width,
            RegionHeight = boundingBox.Height
        };
    }

    public double AdjustFontSize(string translatedText, TextStyleInfo originalStyle)
    {
        var originalLength = translatedText.Length;
        if (originalLength == 0) return originalStyle.FontSize;

        var charWidth = originalStyle.FontSize * 0.6;
        var totalWidth = charWidth * originalLength;

        if (totalWidth <= originalStyle.RegionWidth)
            return originalStyle.FontSize;

        var ratio = originalStyle.RegionWidth / totalWidth;
        return Math.Max(8, originalStyle.FontSize * ratio);
    }
}

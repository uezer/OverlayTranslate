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
    public TextStyleInfo Analyze(Rect boundingBox, string text,
        double baseFontSize = 0, string fontSizeMode = "auto", int customFontSize = 14, Color? backgroundColor = null)
    {
        double fontSize;
        switch (fontSizeMode)
        {
            case "custom":
                fontSize = customFontSize;
                break;
            case "fit-width":
                fontSize = baseFontSize > 0 ? baseFontSize : boundingBox.Height * 0.75;
                var charWidth = fontSize * 0.6;
                var totalWidth = charWidth * text.Length;
                if (totalWidth > boundingBox.Width)
                    fontSize = Math.Max(8, fontSize * (boundingBox.Width / totalWidth));
                break;
            default: // "auto"
                fontSize = baseFontSize > 0 ? baseFontSize : boundingBox.Height * 0.75;
                break;
        }
        fontSize = Math.Max(8, Math.Min(72, fontSize));

        // 根据背景亮度自动选择文字颜色：深色背景用白字，浅色背景用黑字
        var textColor = Colors.Black;
        if (backgroundColor.HasValue)
        {
            var bg = backgroundColor.Value;
            var luminance = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
            textColor = luminance < 128 ? Colors.White : Colors.Black;
        }

        return new TextStyleInfo
        {
            FontSize = fontSize,
            TextColor = textColor,
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

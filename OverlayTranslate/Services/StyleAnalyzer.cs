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
                fontSize = ScaleFontSizeToFit(fontSize, boundingBox.Width, text.Length);
                break;
            default: // "auto"
                fontSize = baseFontSize > 0 ? baseFontSize : boundingBox.Height * 0.75;
                break;
        }
        fontSize = Math.Max(8, fontSize);

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
        if (translatedText.Length == 0) return originalStyle.FontSize;
        return ScaleFontSizeToFit(originalStyle.FontSize, originalStyle.RegionWidth, translatedText.Length);
    }

    public static double ScaleFontSizeToFit(double fontSize, double width, int textLength)
    {
        var charWidth = fontSize * 0.6;
        var totalWidth = charWidth * textLength;
        if (totalWidth <= width) return fontSize;
        return Math.Max(8, fontSize * (width / totalWidth));
    }
}

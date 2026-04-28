using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using OverlayTranslate.Models;
using Rectangle = System.Drawing.Rectangle;

namespace OverlayTranslate.Services;

public sealed class InPlaceRenderer : IInPlaceRenderer
{
    public Bitmap Render(Bitmap original, Rectangle selection, IReadOnlyList<TranslationResult> translations)
    {
        Bitmap output = new(original);

        using Graphics graphics = Graphics.FromImage(output);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        foreach (TranslationResult translation in translations)
        {
            Rectangle textBounds = Normalize(translation.Bounds, selection);
            if (textBounds.Width <= 2 || textBounds.Height <= 2)
            {
                continue;
            }

            Color background = SampleBorderColor(original, textBounds);
            using SolidBrush backgroundBrush = new(background);
            graphics.FillRectangle(backgroundBrush, textBounds);

            RenderStyleEstimate style = EstimateStyle(original, textBounds, background);
            DrawFittedText(graphics, translation.TranslatedText, textBounds, style);
        }

        return output;
    }

    private static Rectangle Normalize(System.Windows.Rect bounds, Rectangle selection)
    {
        int x = selection.X + (int)Math.Round(bounds.X);
        int y = selection.Y + (int)Math.Round(bounds.Y);
        int width = Math.Max(1, (int)Math.Round(bounds.Width));
        int height = Math.Max(1, (int)Math.Round(bounds.Height));
        return new Rectangle(x, y, width, height);
    }

    private static Color SampleBorderColor(Bitmap bitmap, Rectangle region)
    {
        int minX = Math.Max(0, region.Left - 2);
        int minY = Math.Max(0, region.Top - 2);
        int maxX = Math.Min(bitmap.Width - 1, region.Right + 2);
        int maxY = Math.Min(bitmap.Height - 1, region.Bottom + 2);

        long red = 0;
        long green = 0;
        long blue = 0;
        int count = 0;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                bool isBorder = x < region.Left || x >= region.Right || y < region.Top || y >= region.Bottom;
                if (!isBorder)
                {
                    continue;
                }

                Color color = bitmap.GetPixel(x, y);
                red += color.R;
                green += color.G;
                blue += color.B;
                count++;
            }
        }

        if (count == 0)
        {
            return Color.White;
        }

        return Color.FromArgb((int)(red / count), (int)(green / count), (int)(blue / count));
    }

    private static RenderStyleEstimate EstimateStyle(Bitmap bitmap, Rectangle region, Color background)
    {
        long darkRed = 0;
        long darkGreen = 0;
        long darkBlue = 0;
        long lightRed = 0;
        long lightGreen = 0;
        long lightBlue = 0;
        int darkCount = 0;
        int lightCount = 0;

        for (int y = region.Top; y < region.Bottom; y++)
        {
            for (int x = region.Left; x < region.Right; x++)
            {
                Color color = bitmap.GetPixel(x, y);
                int luminance = (color.R + color.G + color.B) / 3;
                if (luminance < 128)
                {
                    darkRed += color.R;
                    darkGreen += color.G;
                    darkBlue += color.B;
                    darkCount++;
                }
                else
                {
                    lightRed += color.R;
                    lightGreen += color.G;
                    lightBlue += color.B;
                    lightCount++;
                }
            }
        }

        int backgroundLuminance = (background.R + background.G + background.B) / 3;
        bool useDarkText = backgroundLuminance >= 140;
        Color textColor;
        if (useDarkText && darkCount > 0)
        {
            textColor = Color.FromArgb((int)(darkRed / darkCount), (int)(darkGreen / darkCount), (int)(darkBlue / darkCount));
        }
        else if (!useDarkText && lightCount > 0)
        {
            textColor = Color.FromArgb((int)(lightRed / lightCount), (int)(lightGreen / lightCount), (int)(lightBlue / lightCount));
        }
        else
        {
            textColor = useDarkText ? Color.Black : Color.White;
        }

        string fontFamily = backgroundLuminance > 150 ? "Segoe UI" : "Microsoft YaHei UI";
        return new RenderStyleEstimate(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(textColor.R, textColor.G, textColor.B)),
            Math.Max(12.0, region.Height * 0.60),
            System.Windows.FontWeights.SemiBold,
            System.Windows.TextAlignment.Left,
            fontFamily,
            region.Height * 0.82);
    }

    private static void DrawFittedText(Graphics graphics, string text, Rectangle bounds, RenderStyleEstimate style)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        float fontSize = (float)style.FontSize;
        FontStyle fontStyle = style.FontWeight >= System.Windows.FontWeights.Bold ? FontStyle.Bold : FontStyle.Regular;
        StringFormat format = new()
        {
            Trimming = StringTrimming.EllipsisWord,
        };

        switch (style.TextAlignment)
        {
            case System.Windows.TextAlignment.Center:
                format.Alignment = StringAlignment.Center;
                break;
            case System.Windows.TextAlignment.Right:
                format.Alignment = StringAlignment.Far;
                break;
            default:
                format.Alignment = StringAlignment.Near;
                break;
        }

        format.LineAlignment = StringAlignment.Near;

        using SolidBrush foregroundBrush = new(ToColor((System.Windows.Media.SolidColorBrush)style.Foreground));

        for (; fontSize >= 8f; fontSize -= 1f)
        {
            using Font font = new(style.FontFamilyName, fontSize, fontStyle, GraphicsUnit.Pixel);
            SizeF measured = graphics.MeasureString(text, font, bounds.Size, format);
            if (measured.Width <= bounds.Width + 3 && measured.Height <= bounds.Height + 3)
            {
                graphics.DrawString(text, font, foregroundBrush, bounds, format);
                return;
            }
        }

        using Font fallbackFont = new(style.FontFamilyName, 8f, fontStyle, GraphicsUnit.Pixel);
        graphics.DrawString(text, fallbackFont, foregroundBrush, bounds, format);
    }

    private static Color ToColor(System.Windows.Media.SolidColorBrush brush)
    {
        return Color.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
    }
}

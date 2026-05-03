using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OverlayTranslate.Services;

public class TextRenderer
{
    public BitmapSource RenderTranslatedText(
        byte[] backgroundImage,
        string translatedText,
        Rect region,
        TextStyleInfo style)
    {
        var bgImage = new BitmapImage();
        using (var stream = new MemoryStream(backgroundImage))
        {
            bgImage.BeginInit();
            bgImage.CacheOption = BitmapCacheOption.OnLoad;
            bgImage.StreamSource = stream;
            bgImage.EndInit();
            bgImage.Freeze();
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(bgImage, new Rect(0, 0, bgImage.PixelWidth, bgImage.PixelHeight));

            var adjustedSize = new StyleAnalyzer().AdjustFontSize(translatedText, style);
            var typeface = new Typeface("Microsoft YaHei");
            var formattedText = new FormattedText(
                translatedText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                adjustedSize,
                new SolidColorBrush(style.TextColor),
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);

            var x = region.X + (region.Width - formattedText.Width) / 2;
            var y = region.Y + (region.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(x, y));
        }

        var renderTarget = new RenderTargetBitmap(
            bgImage.PixelWidth, bgImage.PixelHeight,
            bgImage.DpiX, bgImage.DpiY,
            PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        return renderTarget;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public class TextRenderer
{
    /// <summary>
    /// 渲染多个译文块，每个块在原始文字框位置渲染。
    /// </summary>
    public BitmapSource RenderTranslatedBlocks(
        byte[] backgroundImage,
        IReadOnlyList<(string Text, Rect BoundingBox)> translatedBlocks,
        Rect selection,
        TextStyleInfo style,
        double dpiX, double dpiY,
        double dpiScaleX, double dpiScaleY)
    {
        var bgImage = LoadBackgroundImage(backgroundImage, ref dpiX, ref dpiY);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(bgImage, new Rect(0, 0, bgImage.Width, bgImage.Height));

            var typeface = new Typeface("Microsoft YaHei");
            var pixelsPerDip = (float)(dpiX / 96.0);

            foreach (var (text, bbox) in translatedBlocks)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;

                // bbox 是裁剪区域内的物理像素坐标，转为全屏 DIP 坐标
                var blockDipX = selection.X + bbox.X / dpiScaleX;
                var blockDipY = selection.Y + bbox.Y / dpiScaleY;
                var blockDipW = bbox.Width / dpiScaleX;
                var blockDipH = bbox.Height / dpiScaleY;

                // 基于原始文字框高度估算字号
                var fontSize = blockDipH * 0.75;
                fontSize = Math.Max(8, Math.Min(fontSize, style.FontSize * 1.5));

                // 根据译文长度调整字号（如果译文比原文长太多，缩小以适应）
                var adjustedFontSize = ScaleFontSizeToFit(fontSize, blockDipW, text.Length);

                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    adjustedFontSize,
                    new SolidColorBrush(style.TextColor),
                    pixelsPerDip);

                // 渲染在原始文字框位置（左上角对齐）
                var x = blockDipX;
                var y = blockDipY;

                // 绘制半透明背景提高可读性
                var padding = 2.0;
                var textBgRect = new Rect(
                    x - padding, y - padding,
                    formattedText.Width + padding * 2,
                    formattedText.Height + padding * 2);
                var textBgColor = style.TextColor == Colors.White
                    ? Color.FromArgb(160, 0, 0, 0)
                    : Color.FromArgb(160, 255, 255, 255);
                dc.DrawRectangle(new SolidColorBrush(textBgColor), null, textBgRect);

                dc.DrawText(formattedText, new Point(x, y));
            }
        }

        var renderTarget = new RenderTargetBitmap(
            bgImage.PixelWidth, bgImage.PixelHeight,
            dpiX, dpiY, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    /// <summary>
    /// 渲染单个译文文本块（兼容旧接口）。
    /// </summary>
    public BitmapSource RenderTranslatedText(
        byte[] backgroundImage,
        string translatedText,
        Rect region,
        TextStyleInfo style,
        double dpiX = 96,
        double dpiY = 96)
    {
        var bgImage = LoadBackgroundImage(backgroundImage, ref dpiX, ref dpiY);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(bgImage, new Rect(0, 0, bgImage.Width, bgImage.Height));

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

            var padding = 2.0;
            var textBgRect = new Rect(
                x - padding, y - padding,
                formattedText.Width + padding * 2,
                formattedText.Height + padding * 2);
            var textBgColor = style.TextColor == Colors.White
                ? Color.FromArgb(160, 0, 0, 0)
                : Color.FromArgb(160, 255, 255, 255);
            dc.DrawRectangle(new SolidColorBrush(textBgColor), null, textBgRect);

            dc.DrawText(formattedText, new Point(x, y));
        }

        var renderTarget = new RenderTargetBitmap(
            bgImage.PixelWidth, bgImage.PixelHeight,
            dpiX, dpiY, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    private static BitmapSource LoadBackgroundImage(byte[] backgroundImage, ref double dpiX, ref double dpiY)
    {
        BitmapSource bgImage;
        using (var stream = new MemoryStream(backgroundImage))
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = stream;
            bi.EndInit();
            bi.Freeze();
            bgImage = bi;
        }

        if (dpiX <= 0) dpiX = bgImage.DpiX > 0 ? bgImage.DpiX : 96;
        if (dpiY <= 0) dpiY = bgImage.DpiY > 0 ? bgImage.DpiY : 96;

        if (Math.Abs(bgImage.DpiX - dpiX) > 1 || Math.Abs(bgImage.DpiY - dpiY) > 1)
        {
            var stride = bgImage.PixelWidth * (bgImage.Format.BitsPerPixel / 8);
            var pixels = new byte[stride * bgImage.PixelHeight];
            bgImage.CopyPixels(pixels, stride, 0);
            bgImage = BitmapSource.Create(
                bgImage.PixelWidth, bgImage.PixelHeight,
                dpiX, dpiY,
                bgImage.Format, bgImage.Palette,
                pixels, stride);
            bgImage.Freeze();
        }

        return bgImage;
    }

    private static double ScaleFontSizeToFit(double fontSize, double width, int textLength)
    {
        if (textLength == 0) return fontSize;
        var charWidth = fontSize * 0.6;
        var totalWidth = charWidth * textLength;
        if (totalWidth <= width) return fontSize;
        return Math.Max(8, fontSize * (width / totalWidth));
    }
}

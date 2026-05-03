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
        TextStyleInfo style,
        double dpiX = 96,
        double dpiY = 96)
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

        // 使用传入的原始 DPI（截图时的屏幕 DPI），而非 PNG 解码后的 DPI
        if (dpiX <= 0) dpiX = bgImage.DpiX > 0 ? bgImage.DpiX : 96;
        if (dpiY <= 0) dpiY = bgImage.DpiY > 0 ? bgImage.DpiY : 96;

        // PNG 编码/解码会丢失 DPI 元数据（192→96），导致 DIP 尺寸翻倍。
        // 用正确的 DPI 重新创建 BitmapSource，使 DIP 尺寸匹配原始截图。
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

        Serilog.Log.Information("RenderTranslatedText: bgImage Pixel={PW}x{PH}, DIP={DW}x{DH}, DPI={DpiX}x{DpiY}, region={RX},{RY},{RW},{RH}, text={Text}",
            bgImage.PixelWidth, bgImage.PixelHeight, bgImage.Width, bgImage.Height, bgImage.DpiX, bgImage.DpiY,
            region.X, region.Y, region.Width, region.Height,
            translatedText.Length > 30 ? translatedText[..30] + "..." : translatedText);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // bgImage.Width/Height 现在是正确的 DIP 尺寸（1280x800）
            dc.DrawImage(bgImage, new Rect(0, 0, bgImage.Width, bgImage.Height));

            // region 已经是 WPF 逻辑坐标（DIP），直接使用
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

            // 文字居中在选区内（DIP 坐标）
            var x = region.X + (region.Width - formattedText.Width) / 2;
            var y = region.Y + (region.Height - formattedText.Height) / 2;

            // 绘制半透明文字背景，提高可读性
            var padding = 2.0;
            var textBgRect = new Rect(
                x - padding,
                y - padding,
                formattedText.Width + padding * 2,
                formattedText.Height + padding * 2);
            var textBgColor = style.TextColor == Colors.White
                ? Color.FromArgb(160, 0, 0, 0)    // 深色背景：半透明黑色底
                : Color.FromArgb(160, 255, 255, 255); // 浅色背景：半透明白色底
            dc.DrawRectangle(new SolidColorBrush(textBgColor), null, textBgRect);

            Serilog.Log.Information("RenderTranslatedText: fontSize={Size}, textDIP={TW}x{TH}, pos={X},{Y}, textColor={TC}",
                adjustedSize, formattedText.Width, formattedText.Height, x, y, style.TextColor);
            dc.DrawText(formattedText, new Point(x, y));
        }

        // 用图像的实际 DPI 创建 RenderTargetBitmap，保持像素级精确
        var renderTarget = new RenderTargetBitmap(
            bgImage.PixelWidth, bgImage.PixelHeight,
            dpiX, dpiY,
            PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        Serilog.Log.Information("RenderTranslatedText: output RTB {PW}x{PH}, DPI={DpiX}x{DpiY}, Format={Format}",
            renderTarget.PixelWidth, renderTarget.PixelHeight, renderTarget.DpiX, renderTarget.DpiY,
            renderTarget.Format);

        // 验证输出不是全黑：检查中心像素
        var testPixels = new byte[4];
        var cx = renderTarget.PixelWidth / 2;
        var cy = renderTarget.PixelHeight / 2;
        renderTarget.CopyPixels(new System.Windows.Int32Rect(cx, cy, 1, 1), testPixels, 4, 0);
        Serilog.Log.Information("RenderTranslatedText: center pixel at ({CX},{CY}) = B={B} G={G} R={R} A={A}",
            cx, cy, testPixels[0], testPixels[1], testPixels[2], testPixels[3]);

        // 检查选区内像素
        var selPx = (int)((region.X + region.Width / 2) * renderTarget.PixelWidth / bgImage.Width);
        var selPy = (int)((region.Y + region.Height / 2) * renderTarget.PixelHeight / bgImage.Height);
        if (selPx >= 0 && selPx < renderTarget.PixelWidth && selPy >= 0 && selPy < renderTarget.PixelHeight)
        {
            renderTarget.CopyPixels(new System.Windows.Int32Rect(selPx, selPy, 1, 1), testPixels, 4, 0);
            Serilog.Log.Information("RenderTranslatedText: selection pixel at ({PX},{PY}) = B={B} G={G} R={R} A={A}",
                selPx, selPy, testPixels[0], testPixels[1], testPixels[2], testPixels[3]);
        }

        return renderTarget;
    }
}

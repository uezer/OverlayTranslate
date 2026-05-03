using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace OverlayTranslate.Services;

public class ScreenshotService
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private static double GetScreenDpiX()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            return GetDeviceCaps(hdc, LOGPIXELSX);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static double GetScreenDpiY()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            return GetDeviceCaps(hdc, LOGPIXELSY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// 截取主屏幕全屏（物理像素），并写入正确的 DPI 元数据。
    /// </summary>
    public byte[] CaptureFullScreen()
    {
        var desktop = GetDesktopWindow();
        GetWindowRect(desktop, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));

        // 写入屏幕实际 DPI
        var dpiX = GetScreenDpiX();
        var dpiY = GetScreenDpiY();
        bitmap.SetResolution((float)dpiX, (float)dpiY);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// 截取指定区域（WPF 逻辑坐标 → 物理像素）。
    /// </summary>
    public byte[] CaptureRegion(Rect region)
    {
        var dpiX = GetScreenDpiX();
        var dpiY = GetScreenDpiY();
        var scaleX = dpiX / 96.0;
        var scaleY = dpiY / 96.0;

        var px = (int)(region.X * scaleX);
        var py = (int)(region.Y * scaleY);
        var pw = (int)(region.Width * scaleX);
        var ph = (int)(region.Height * scaleY);

        using var bitmap = new Bitmap(pw, ph);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(px, py, 0, 0, new System.Drawing.Size(pw, ph));
        bitmap.SetResolution((float)dpiX, (float)dpiY);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// 从已有全屏截图中裁剪选区。选区坐标为 WPF 逻辑坐标，转换为图像物理像素。
    /// </summary>
    public byte[] CropRegion(byte[] fullScreenshot, Rect region)
    {
        using var stream = new MemoryStream(fullScreenshot);
        using var original = Image.FromStream(stream);

        var imgWidthPx = original.Width;
        var imgHeightPx = original.Height;

        var imgDpiX = (double)original.HorizontalResolution;
        var imgDpiY = (double)original.VerticalResolution;
        if (imgDpiX <= 0) imgDpiX = 96;
        if (imgDpiY <= 0) imgDpiY = 96;

        var scaleX = imgDpiX / 96.0;
        var scaleY = imgDpiY / 96.0;

        var sx = Math.Max(0, (int)(region.X * scaleX));
        var sy = Math.Max(0, (int)(region.Y * scaleY));
        var sw = Math.Min(imgWidthPx - sx, (int)(region.Width * scaleX));
        var sh = Math.Min(imgHeightPx - sy, (int)(region.Height * scaleY));

        if (sw <= 0 || sh <= 0) return fullScreenshot;

        using var cropped = new Bitmap(sw, sh);
        cropped.SetResolution((float)imgDpiX, (float)imgDpiY);
        using var graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(original,
            new Rectangle(0, 0, sw, sh),
            new Rectangle(sx, sy, sw, sh),
            GraphicsUnit.Pixel);

        using var outputStream = new MemoryStream();
        cropped.Save(outputStream, ImageFormat.Png);
        return outputStream.ToArray();
    }
}

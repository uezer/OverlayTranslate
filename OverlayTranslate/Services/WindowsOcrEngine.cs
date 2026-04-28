using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using OverlayTranslate.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinOcrLine = Windows.Media.Ocr.OcrLine;
using WinOcrWord = Windows.Media.Ocr.OcrWord;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;

namespace OverlayTranslate.Services;

/// <summary>
/// OCR engine backed by Windows.Media.Ocr — accurate for Latin scripts (English, etc.)
/// and available on all Windows 10+ machines without external dependencies.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    public async Task<IReadOnlyList<OcrBlock>> RecognizeAsync(
        Bitmap bitmap,
        SourceLanguage sourceLanguage,
        CancellationToken cancellationToken)
    {
        AppLogger.Info($"WindowsOcrEngine.RecognizeAsync started. SourceLanguage={sourceLanguage}, Size={bitmap.Width}x{bitmap.Height}.");

        OcrEngine? engine = CreateEngine(sourceLanguage);
        if (engine is null)
        {
            AppLogger.Warn("WindowsOcrEngine: no suitable OCR language pack found, returning empty result.");
            return [];
        }

        using Bitmap preprocessed = PrepareForRecognition(bitmap, out float scale);
        SoftwareBitmap softwareBitmap = await ConvertToSoftwareBitmapAsync(preprocessed, cancellationToken).ConfigureAwait(false);
        using (softwareBitmap)
        {
            OcrResult result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);
            AppLogger.Info($"WindowsOcrEngine: recognized {result.Lines.Count} line(s).");
            return MapResult(result, scale);
        }
    }

    private static Bitmap PrepareForRecognition(Bitmap bitmap, out float scale)
    {
        // Windows OCR tends to miss very small text. Upscale small selections before recognition.
        int minSide = Math.Min(bitmap.Width, bitmap.Height);
        if (minSide >= 80)
        {
            scale = 1f;
            return new Bitmap(bitmap);
        }

        scale = minSide < 50 ? 3f : 2f;
        int targetWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        Bitmap enlarged = new(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(enlarged);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(bitmap, new Rectangle(0, 0, targetWidth, targetHeight));
        return enlarged;
    }

    private static OcrEngine? CreateEngine(SourceLanguage sourceLanguage)
    {
        // Try the requested language, then fall back to English, then any available language.
        string[] candidates = sourceLanguage switch
        {
            SourceLanguage.English  => ["en-US", "en-GB", "en"],
            SourceLanguage.Chinese  => ["zh-Hans-CN", "zh-Hant-TW", "zh"],
            SourceLanguage.Japanese => ["ja-JP", "ja"],
            _                       => ["en-US", "en-GB", "en"],
        };

        foreach (string tag in candidates)
        {
            OcrEngine? engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
            if (engine is not null)
            {
                return engine;
            }
        }

        // Last resort: system user-profile languages.
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(
        Bitmap bitmap, CancellationToken cancellationToken)
    {
        using MemoryStream ms = new();
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;

        using Windows.Storage.Streams.InMemoryRandomAccessStream ras = new();
        using (Windows.Storage.Streams.DataWriter writer = new(ras))
        {
            writer.WriteBytes(ms.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            writer.DetachStream();
        }

        ras.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ras).AsTask(cancellationToken).ConfigureAwait(false);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
            .AsTask(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<OcrBlock> MapResult(OcrResult result, float scale)
    {
        List<OcrBlock> blocks = [];
        double invScale = scale <= 0 ? 1d : 1d / scale;

        foreach (WinOcrLine line in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            // Compute the bounding box of this line from its words.
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            List<Models.OcrLine> modelLines = [];
            foreach (WinOcrWord word in line.Words)
            {
                Windows.Foundation.Rect wr = word.BoundingRect;
                if (wr.X < minX) minX = wr.X;
                if (wr.Y < minY) minY = wr.Y;
                if (wr.X + wr.Width > maxX)  maxX = wr.X + wr.Width;
                if (wr.Y + wr.Height > maxY) maxY = wr.Y + wr.Height;
            }

            if (minX == double.MaxValue)
            {
                continue; // no words
            }

            double x = minX * invScale;
            double y = minY * invScale;
            double width = (maxX - minX) * invScale;
            double height = (maxY - minY) * invScale;
            Rect bounds = new(x, y, width, height);
            IReadOnlyList<Point> polygon =
            [
                new Point(x, y),
                new Point(x + width, y),
                new Point(x + width, y + height),
                new Point(x, y + height),
            ];

            modelLines.Add(new Models.OcrLine(bounds, polygon, line.Text, Confidence: 1.0f));
            blocks.Add(new OcrBlock(bounds, modelLines, line.Text));
        }

        return blocks;
    }
}

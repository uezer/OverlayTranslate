using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using OverlayTranslate.Models;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;

namespace OverlayTranslate.Services;

public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<OcrBlock>> RecognizeAsync(Bitmap bitmap, SourceLanguage sourceLanguage, CancellationToken cancellationToken)
    {
        AppLogger.Info($"OCR worker request started. SourceLanguage={sourceLanguage}, Bitmap={bitmap.Width}x{bitmap.Height}.");
        using Bitmap preparedBitmap = PrepareForRecognition(bitmap, out double preparedScale);
        IReadOnlyList<OcrBlock> firstAttempt = await RunSingleAttemptAsync(
            preparedBitmap,
            preparedScale,
            sourceLanguage,
            "upscaled",
            cancellationToken);
        if (HasUsableText(firstAttempt))
        {
            return firstAttempt;
        }

        AppLogger.Warn("Paddle OCR first attempt returned empty/whitespace text. Retrying with original bitmap.");
        using Bitmap originalBitmap = new(bitmap);
        IReadOnlyList<OcrBlock> secondAttempt = await RunSingleAttemptAsync(
            originalBitmap,
            1d,
            sourceLanguage,
            "original",
            cancellationToken);
        if (HasUsableText(secondAttempt))
        {
            return secondAttempt;
        }

        AppLogger.Warn("Paddle OCR second attempt still empty. Retrying with high-contrast preprocessing.");
        using Bitmap highContrastBitmap = PrepareHighContrastForRecognition(bitmap, out double highContrastScale);
        IReadOnlyList<OcrBlock> thirdAttempt = await RunSingleAttemptAsync(
            highContrastBitmap,
            highContrastScale,
            sourceLanguage,
            "high-contrast",
            cancellationToken);
        if (!HasUsableText(thirdAttempt))
        {
            SaveDebugBitmaps(bitmap, preparedBitmap, highContrastBitmap);
        }

        return thirdAttempt;
    }

    public void Dispose()
    {
    }

    private static Process StartWorkerProcess(string inputPath, string outputPath, SourceLanguage sourceLanguage)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve the current executable path.");

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            Arguments = $"--ocr-worker --input \"{inputPath}\" --output \"{outputPath}\" --source {ToSourceCode(sourceLanguage)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the OCR worker process.");
        AppLogger.Info($"OCR worker process started. PID={process.Id}, Input={inputPath}.");
        return process;
    }

    private static async Task<IReadOnlyList<OcrBlock>> RunSingleAttemptAsync(
        Bitmap inputBitmap,
        double scaleFactor,
        SourceLanguage sourceLanguage,
        string attemptName,
        CancellationToken cancellationToken)
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "OverlayTranslate",
            "ocr",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        string inputPath = Path.Combine(tempDirectory, "input.png");
        string outputPath = Path.Combine(tempDirectory, "output.json");
        inputBitmap.Save(inputPath, ImageFormat.Png);

        try
        {
            using Process process = StartWorkerProcess(inputPath, outputPath, sourceLanguage);
            using CancellationTokenRegistration _ = cancellationToken.Register(() => TryKill(process));

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            AppLogger.Info($"OCR worker exited. Attempt={attemptName}, ExitCode={process.ExitCode}.");

            string standardError = await stderrTask;
            if (process.ExitCode != 0)
            {
                string message = string.IsNullOrWhiteSpace(standardError)
                    ? $"OCR worker exited with code {process.ExitCode}."
                    : standardError.Trim();
                throw new InvalidOperationException(message);
            }

            if (!File.Exists(outputPath))
            {
                string standardOutput = await stdoutTask;
                string message = string.IsNullOrWhiteSpace(standardOutput)
                    ? "OCR worker did not produce an output file."
                    : standardOutput.Trim();
                throw new InvalidOperationException(message);
            }

            await using FileStream stream = File.OpenRead(outputPath);
            OcrWorkerResult? payload = await JsonSerializer.DeserializeAsync<OcrWorkerResult>(stream, JsonOptions, cancellationToken);
            AppLogger.Info($"OCR worker output parsed. Attempt={attemptName}, Blocks={payload?.Blocks?.Count ?? 0}.");
            IReadOnlyList<OcrBlock> blocks = MapBlocks(payload, scaleFactor);
            int nonEmptyCount = blocks.Count(block => !string.IsNullOrWhiteSpace(block.Text));
            AppLogger.Info($"OCR mapping finished. Attempt={attemptName}, NonEmptyBlocks={nonEmptyCount}/{blocks.Count}.");
            return blocks;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static Bitmap PrepareForRecognition(Bitmap bitmap, out double scaleFactor)
    {
        int minSide = Math.Min(bitmap.Width, bitmap.Height);
        if (minSide >= 80)
        {
            scaleFactor = 1d;
            return new Bitmap(bitmap);
        }

        scaleFactor = minSide < 50 ? 3d : 2d;
        int targetWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scaleFactor));
        int targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scaleFactor));

        Bitmap enlarged = new(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(enlarged);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(bitmap, new Rectangle(0, 0, targetWidth, targetHeight));

        AppLogger.Info($"Paddle OCR preprocessing: upscaled by {scaleFactor:0.0}x to {targetWidth}x{targetHeight}.");
        return enlarged;
    }

    private static Bitmap PrepareHighContrastForRecognition(Bitmap bitmap, out double scaleFactor)
    {
        using Bitmap upscaled = PrepareForRecognition(bitmap, out scaleFactor);
        Bitmap output = new(upscaled.Width, upscaled.Height, PixelFormat.Format24bppRgb);

        double luminanceSum = 0d;
        int pixelCount = upscaled.Width * upscaled.Height;
        for (int y = 0; y < upscaled.Height; y++)
        {
            for (int x = 0; x < upscaled.Width; x++)
            {
                Color c = upscaled.GetPixel(x, y);
                luminanceSum += (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
            }
        }

        int threshold = pixelCount == 0 ? 128 : (int)Math.Round(luminanceSum / pixelCount);
        threshold = Math.Clamp(threshold, 90, 180);

        for (int y = 0; y < upscaled.Height; y++)
        {
            for (int x = 0; x < upscaled.Width; x++)
            {
                Color c = upscaled.GetPixel(x, y);
                double luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
                output.SetPixel(x, y, luminance >= threshold ? Color.White : Color.Black);
            }
        }

        AppLogger.Info($"Paddle OCR preprocessing: generated high-contrast bitmap with threshold={threshold}.");
        return output;
    }

    private static IReadOnlyList<OcrBlock> MapBlocks(OcrWorkerResult? payload, double scaleFactor)
    {
        if (payload?.Blocks is null || payload.Blocks.Count == 0)
        {
            return [];
        }

        double inverseScale = scaleFactor <= 0 ? 1d : 1d / scaleFactor;
        List<OcrBlock> blocks = [];
        foreach (OcrWorkerBlock block in payload.Blocks)
        {
            string normalizedText = NormalizeOcrText(block.Text);
            IReadOnlyList<Point> polygon = (block.Polygon ?? [])
                .Select(point => new Point(point.X * inverseScale, point.Y * inverseScale))
                .ToArray();

            Rect bounds = new(
                block.X * inverseScale,
                block.Y * inverseScale,
                block.Width * inverseScale,
                block.Height * inverseScale);
            OcrLine line = new(bounds, polygon, normalizedText, block.Confidence);
            blocks.Add(new OcrBlock(bounds, [line], normalizedText));
        }

        return blocks;
    }

    private static string NormalizeOcrText(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        StringBuilder builder = new(rawText.Length);
        bool lastWasWhitespace = false;
        foreach (char ch in rawText)
        {
            if (char.IsControl(ch) || ch == '\u200B' || ch == '\u200C' || ch == '\u200D' || ch == '\uFEFF')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasWhitespace)
                {
                    builder.Append(' ');
                    lastWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool HasUsableText(IReadOnlyList<OcrBlock> blocks)
    {
        return blocks.Any(block => !string.IsNullOrWhiteSpace(block.Text));
    }

    private static void SaveDebugBitmaps(Bitmap original, Bitmap upscaled, Bitmap highContrast)
    {
        try
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "OverlayTranslate",
                "ocr-debug",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(directory);

            string originalPath = Path.Combine(directory, "original.png");
            string upscaledPath = Path.Combine(directory, "upscaled.png");
            string highContrastPath = Path.Combine(directory, "high-contrast.png");

            original.Save(originalPath, ImageFormat.Png);
            upscaled.Save(upscaledPath, ImageFormat.Png);
            highContrast.Save(highContrastPath, ImageFormat.Png);

            AppLogger.Warn($"Paddle OCR still returned empty text. Debug images saved to: {directory}");
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to save OCR debug images: {exception.Message}");
        }
    }

    private static string ToSourceCode(SourceLanguage sourceLanguage)
    {
        return sourceLanguage switch
        {
            SourceLanguage.Chinese => "zh",
            SourceLanguage.English => "en",
            SourceLanguage.Japanese => "ja",
            _ => "auto",
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
        catch
        {
        }
    }

    private sealed class OcrWorkerResult
    {
        public List<OcrWorkerBlock>? Blocks { get; set; }
    }

    private sealed class OcrWorkerBlock
    {
        public string? Text { get; set; }

        public float Confidence { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public List<OcrWorkerPoint>? Polygon { get; set; }
    }

    private sealed class OcrWorkerPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }
}

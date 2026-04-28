using System.Drawing;
using System.IO;
using System.Text.Json;
using PaddleOCRSharp;

namespace OverlayTranslate.Services;

public static class OcrWorkerEntryPoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static bool IsWorkerInvocation(string[] args)
    {
        return args.Any(argument => string.Equals(argument, "--ocr-worker", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            WorkerArguments workerArguments = ParseArguments(args);
            AppLogger.Info($"OCR worker running. Input={workerArguments.InputPath}, Output={workerArguments.OutputPath}.");
            await ExecuteAsync(workerArguments);
            AppLogger.Info("OCR worker completed successfully.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn("OCR worker canceled.");
            Console.Error.WriteLine("OCR worker was canceled.");
            return 2;
        }
        catch (Exception exception)
        {
            AppLogger.Error("OCR worker failed.", exception);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task ExecuteAsync(WorkerArguments arguments)
    {
        if (!File.Exists(arguments.InputPath))
        {
            throw new FileNotFoundException("OCR input image was not found.", arguments.InputPath);
        }

        using Bitmap bitmap = new(arguments.InputPath);
        PaddleOCREngine engine = new();
        OCRResult result = engine.DetectText(bitmap);

        WorkerResult payload = new()
        {
            Blocks = (result.TextBlocks ?? [])
                .Select(ToBlock)
                .ToList(),
        };

        string? outputDirectory = Path.GetDirectoryName(arguments.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using FileStream stream = File.Create(arguments.OutputPath);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions);
    }

    private static WorkerBlock ToBlock(TextBlock block)
    {
        List<WorkerPoint> polygon = (block.BoxPoints ?? [])
            .Select(point => new WorkerPoint
            {
                X = point.X,
                Y = point.Y,
            })
            .ToList();

        double minX = polygon.Count == 0 ? 0 : polygon.Min(point => point.X);
        double minY = polygon.Count == 0 ? 0 : polygon.Min(point => point.Y);
        double maxX = polygon.Count == 0 ? 0 : polygon.Max(point => point.X);
        double maxY = polygon.Count == 0 ? 0 : polygon.Max(point => point.Y);

        return new WorkerBlock
        {
            Text = block.Text ?? string.Empty,
            Confidence = block.Score,
            X = minX,
            Y = minY,
            Width = Math.Max(0, maxX - minX),
            Height = Math.Max(0, maxY - minY),
            Polygon = polygon,
        };
    }

    private static WorkerArguments ParseArguments(string[] args)
    {
        string? inputPath = null;
        string? outputPath = null;

        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--input", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                inputPath = args[++index];
                continue;
            }

            if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputPath = args[++index];
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("OCR worker requires --input and --output arguments.");
        }

        return new WorkerArguments(inputPath, outputPath);
    }

    private sealed record WorkerArguments(string InputPath, string OutputPath);

    private sealed class WorkerResult
    {
        public List<WorkerBlock> Blocks { get; set; } = [];
    }

    private sealed class WorkerBlock
    {
        public string Text { get; set; } = string.Empty;

        public float Confidence { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public List<WorkerPoint> Polygon { get; set; } = [];
    }

    private sealed class WorkerPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;

namespace OverlayTranslate.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OverlayTranslate",
        "logs");

    public static string CurrentLogPath
    {
        get
        {
            Directory.CreateDirectory(LogDirectory);
            return Path.Combine(LogDirectory, $"{DateTime.Now:yyyyMMdd}.log");
        }
    }

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Warn(string message)
    {
        Write("WARN", message, null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        string line = BuildLine(level, message, exception);
        Debug.WriteLine(line);

        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(CurrentLogPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string BuildLine(string level, string message, Exception? exception)
    {
        StringBuilder builder = new();
        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.Append(" [");
        builder.Append(level);
        builder.Append("] ");
        builder.Append(message);

        if (exception is not null)
        {
            builder.Append(" | ");
            builder.Append(exception.GetType().Name);
            builder.Append(": ");
            builder.Append(exception.Message);
            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                builder.AppendLine();
                builder.Append(exception.StackTrace);
            }
        }

        return builder.ToString();
    }
}

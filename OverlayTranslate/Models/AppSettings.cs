namespace OverlayTranslate.Models;

public class AppSettings
{
    public OcrSettings Ocr { get; set; } = new();
    public TranslationSettings Translation { get; set; } = new();
    public HotkeySettings Hotkey { get; set; } = new();
    public LanguageSettings Language { get; set; } = new();
    public PythonSettings Python { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public OtherSettings Other { get; set; } = new();
    public UpdateSettings Update { get; set; } = new();
}

public class OcrSettings
{
    public string ActiveEngine { get; set; } = "PaddleOCR";
    public string? FallbackEngine { get; set; }
    public string Strategy { get; set; } = "LocalFirst";
    public Dictionary<string, Dictionary<string, string>> Engines { get; set; } = new();
}

public class TranslationSettings
{
    public string ActiveEngine { get; set; } = "Google";
    public string? FallbackEngine { get; set; }
    public string Strategy { get; set; } = "LocalFirst";
    public Dictionary<string, Dictionary<string, string>> Engines { get; set; } = new();
}

public class HotkeySettings
{
    public string[] Modifiers { get; set; } = ["Ctrl", "Shift"];
    public string Key { get; set; } = "T";
}

public class LanguageSettings
{
    public string Source { get; set; } = "auto";
    public string Target { get; set; } = "zh-CN";
}

public class PythonSettings
{
    public string RuntimePath { get; set; } = "";
}

public class LoggingSettings
{
    public string Level { get; set; } = "Information";
    public string File { get; set; } = "logs/app.log";
}

public class OtherSettings
{
    public string FontSizeMode { get; set; } = "auto"; // auto / fit-width / custom
    public int CustomFontSize { get; set; } = 14;
    public string Theme { get; set; } = "system"; // light / dark / system
    public string Locale { get; set; } = "";  // "" = 跟随系统, "zh-CN", "en-US"
}

public class UpdateSettings
{
    public bool AutoCheck { get; set; } = true;
    public string? SkippedVersion { get; set; }
}

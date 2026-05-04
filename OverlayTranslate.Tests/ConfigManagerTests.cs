using System.IO;
using System.Text.Json;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Models;

namespace OverlayTranslate.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _configDir;
    private readonly string _configPath;

    public ConfigManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"OverlayTranslateTests_{Guid.NewGuid():N}");
        _configDir = Path.Combine(_testDir, "Config");
        _configPath = Path.Combine(_configDir, "appsettings.json");
        Directory.CreateDirectory(_configDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private ConfigManager CreateManager()
    {
        // 使用测试专用的配置路径
        return new ConfigManager(_configPath);
    }

    [Fact]
    public void Settings_DefaultValues_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal("PaddleOCR", settings.Ocr.ActiveEngine);
        Assert.Equal("Google", settings.Translation.ActiveEngine);
        Assert.Equal("auto", settings.Language.Source);
        Assert.Equal("zh-CN", settings.Language.Target);
        Assert.Equal("Information", settings.Logging.Level);
        Assert.Equal("logs/app.log", settings.Logging.File);
    }

    [Fact]
    public void HotkeySettings_DefaultModifiers_AreCtrlShift()
    {
        var settings = new HotkeySettings();

        Assert.Equal(["Ctrl", "Shift"], settings.Modifiers);
        Assert.Equal("T", settings.Key);
    }

    [Fact]
    public void OcrSettings_DefaultStrategy_IsLocalFirst()
    {
        var settings = new OcrSettings();

        Assert.Equal("LocalFirst", settings.Strategy);
        Assert.NotNull(settings.Engines);
        Assert.Empty(settings.Engines);
    }

    [Fact]
    public void TranslationSettings_DefaultStrategy_IsLocalFirst()
    {
        var settings = new TranslationSettings();

        Assert.Equal("LocalFirst", settings.Strategy);
        Assert.NotNull(settings.Engines);
        Assert.Empty(settings.Engines);
    }

    [Fact]
    public void PythonSettings_DefaultRuntimePath_IsEmpty()
    {
        var settings = new PythonSettings();

        Assert.Equal("", settings.RuntimePath);
    }

    [Fact]
    public void AppSettings_EnginesDictionaries_AreInitialized()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.Ocr.Engines);
        Assert.NotNull(settings.Translation.Engines);
    }

    [Fact]
    public void Settings_CanBeSerializedAndDeserialized()
    {
        var original = new AppSettings
        {
            Ocr = new OcrSettings { ActiveEngine = "RemoteOCR" },
            Translation = new TranslationSettings { ActiveEngine = "Google" },
            Language = new LanguageSettings { Source = "en", Target = "ja" }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("RemoteOCR", deserialized.Ocr.ActiveEngine);
        Assert.Equal("Google", deserialized.Translation.ActiveEngine);
        Assert.Equal("en", deserialized.Language.Source);
        Assert.Equal("ja", deserialized.Language.Target);
    }

    [Fact]
    public void Settings_WithEngineConfig_PreservesNestedDictionary()
    {
        var settings = new AppSettings();
        settings.Translation.Engines["DeepL"] = new Dictionary<string, string>
        {
            ["apiKey"] = "test-key",
            ["freeTier"] = "true"
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(settings, options);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json, options);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Translation.Engines.ContainsKey("DeepL"));
        Assert.Equal("test-key", deserialized.Translation.Engines["DeepL"]["apiKey"]);
        Assert.Equal("true", deserialized.Translation.Engines["DeepL"]["freeTier"]);
    }
}

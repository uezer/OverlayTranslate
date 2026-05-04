using System.IO;
using System.Text.Json;
using OverlayTranslate.Models;

namespace OverlayTranslate.Infrastructure;

public class ConfigManager
{
    private readonly string _configPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Settings { get; private set; } = new();

    public ConfigManager() : this(GetDefaultConfigPath())
    {
    }

    public ConfigManager(string configPath)
    {
        _configPath = configPath;
    }

    private static string GetDefaultConfigPath()
    {
        // 优先使用用户应用数据目录（解决C盘安装时的权限问题）
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appDataPath, "OverlayTranslate", "Config");
        return Path.Combine(configDir, "appsettings.json");
    }

    public void Load()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Settings = new AppSettings();
            }
        }
        else
        {
            Save(); // 写入默认配置
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(_configPath, json);
    }
}

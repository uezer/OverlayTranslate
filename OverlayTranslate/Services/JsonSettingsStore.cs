using System.Text.Json;
using System.IO;
using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public JsonSettingsStore()
    {
        string baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OverlayTranslate");
        Directory.CreateDirectory(baseDirectory);
        _settingsPath = Path.Combine(baseDirectory, "settings.json");
    }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            await SaveAsync(new AppSettings()).ConfigureAwait(false);
        }
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using FileStream stream = File.OpenRead(_settingsPath);
        AppSettings? settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        string tempPath = $"{_settingsPath}.tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
        }

        File.Move(tempPath, _settingsPath, true);
    }
}

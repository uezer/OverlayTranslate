using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public interface ISettingsStore
{
    Task InitializeAsync();

    Task<AppSettings> LoadAsync();

    Task SaveAsync(AppSettings settings);
}

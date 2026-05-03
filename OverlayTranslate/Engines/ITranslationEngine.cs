using OverlayTranslate.Models;

namespace OverlayTranslate.Engines;

public interface ITranslationEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken ct = default);
    string[] GetSupportedLanguages();
}

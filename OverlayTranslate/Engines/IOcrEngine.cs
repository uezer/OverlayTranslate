using OverlayTranslate.Models;

namespace OverlayTranslate.Engines;

public interface IOcrEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<OcrResult> RecognizeAsync(byte[] imageData, string language = "auto", CancellationToken ct = default);
    string[] GetSupportedLanguages();
}

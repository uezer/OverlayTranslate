using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationResult>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);
}

using System.Drawing;
using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

/// <summary>
/// OCR reliability wrapper: try Windows OCR first, then fall back to Paddle OCR when
/// Windows OCR fails or returns no text.
/// </summary>
public sealed class FallbackOcrEngine : IOcrEngine, IDisposable
{
    private readonly IOcrEngine _primary;
    private readonly IOcrEngine _fallback;

    public FallbackOcrEngine(IOcrEngine primary, IOcrEngine fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public async Task<IReadOnlyList<OcrBlock>> RecognizeAsync(Bitmap bitmap, SourceLanguage sourceLanguage, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<OcrBlock> primaryBlocks = await _primary.RecognizeAsync(bitmap, sourceLanguage, cancellationToken).ConfigureAwait(false);
            int primaryTextLength = primaryBlocks.Sum(block => block.Text?.Length ?? 0);
            if (primaryBlocks.Count > 0 && primaryTextLength > 0)
            {
                AppLogger.Info($"FallbackOcrEngine: primary OCR succeeded. Blocks={primaryBlocks.Count}");
                return primaryBlocks;
            }

            AppLogger.Warn("FallbackOcrEngine: primary OCR returned empty result, switching to fallback OCR.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"FallbackOcrEngine: primary OCR failed ({ex.GetType().Name}: {ex.Message}), switching to fallback OCR.");
        }

        IReadOnlyList<OcrBlock> fallbackBlocks = await _fallback.RecognizeAsync(bitmap, sourceLanguage, cancellationToken).ConfigureAwait(false);
        AppLogger.Info($"FallbackOcrEngine: fallback OCR finished. Blocks={fallbackBlocks.Count}");
        return fallbackBlocks;
    }

    public void Dispose()
    {
        if (_primary is IDisposable p)
        {
            p.Dispose();
        }

        if (_fallback is IDisposable f)
        {
            f.Dispose();
        }
    }
}

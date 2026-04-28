using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OverlayTranslate.Models;

namespace OverlayTranslate.Services;

public sealed class OrchestratingTranslationProvider : ITranslationProvider, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly HttpClient _httpClient;
    private readonly SidecarManager _sidecarManager;

    public OrchestratingTranslationProvider(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        _sidecarManager = new SidecarManager();
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        string normalizedSource = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
        string normalizedTarget = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage;
        AppLogger.Info($"TranslateAsync entered. Strategy={settings.TranslationStrategy}, Segments={segments.Count}, Source={normalizedSource}, Target={normalizedTarget}.");

        return settings.TranslationStrategy switch
        {
            TranslationStrategy.LocalOnly => await TranslateWithLocalAsync(segments, normalizedSource, normalizedTarget, cancellationToken).ConfigureAwait(false),
            TranslationStrategy.OnlineOnly => await TranslateWithOnlineAsync(settings, segments, normalizedSource, normalizedTarget, cancellationToken).ConfigureAwait(false),
            _ => await TranslateLocalFirstAsync(settings, segments, normalizedSource, normalizedTarget, cancellationToken).ConfigureAwait(false),
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _sidecarManager.Dispose();
    }

    private async Task<IReadOnlyList<TranslationResult>> TranslateLocalFirstAsync(
        AppSettings settings,
        IReadOnlyList<TranslationSegment> segments,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        try
        {
            AppLogger.Info("Translation strategy LocalFirstThenOnline: trying local sidecar first.");
            return await TranslateWithLocalAsync(segments, sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Local translation failed, falling back to online. Reason={exception.Message}");
            return await TranslateWithOnlineAsync(settings, segments, sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<TranslationResult>> TranslateWithLocalAsync(
        IReadOnlyList<TranslationSegment> segments,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        AppLogger.Info("TranslateWithLocalAsync: ensuring local sidecar.");
        await _sidecarManager.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        AppLogger.Info("TranslateWithLocalAsync: local sidecar healthy, sending request.");
        return await TranslateCoreAsync(_sidecarManager.BaseUri, segments, sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TranslationResult>> TranslateWithOnlineAsync(
        AppSettings settings,
        IReadOnlyList<TranslationSegment> segments,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.OnlineTranslationEndpoint))
        {
            AppLogger.Warn("Online translation requested but endpoint is empty.");
            throw new InvalidOperationException("Online translation endpoint is not configured.");
        }

        Uri endpoint = new(settings.OnlineTranslationEndpoint.TrimEnd('/') + "/");
        AppLogger.Info($"TranslateWithOnlineAsync: sending request to {endpoint}.");
        return await TranslateCoreAsync(endpoint, segments, sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TranslationResult>> TranslateCoreAsync(
        Uri baseUri,
        IReadOnlyList<TranslationSegment> segments,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        string[] textArray = segments.Select(segment => segment.SourceText).ToArray();
        TranslateRequest request = new()
        {
            Q = textArray,
            Source = sourceLanguage,
            Target = targetLanguage,
        };

        Uri translateUri = new(baseUri, "translate");
        AppLogger.Info($"TranslateCoreAsync POST {translateUri}.");
        // Serialize explicitly so StringContent sets Content-Length.
        // Python's BaseHTTPRequestHandler relies on Content-Length to read the body;
        // PostAsJsonAsync can emit chunked transfer-encoding which omits that header.
        string requestJson = JsonSerializer.Serialize(request);
        using StringContent httpContent = new(requestJson, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(translateUri, httpContent, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            AppLogger.Error($"HTTP request failed for translation endpoint {translateUri}.", exception);
            throw CreateTranslationConnectivityException(baseUri, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            AppLogger.Error($"Translation request timed out for endpoint {translateUri}.", exception);
            throw new InvalidOperationException($"翻译服务请求超时：{translateUri}", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                AppLogger.Warn($"Translation service returned HTTP {(int)response.StatusCode}. Body={errorBody}");
                throw new InvalidOperationException($"翻译服务返回错误（HTTP {(int)response.StatusCode}）。");
            }

            TranslateResponse? payload = await response.Content.ReadFromJsonAsync<TranslateResponse>(cancellationToken).ConfigureAwait(false);
            if (payload?.TranslatedText is null)
            {
                AppLogger.Warn("Translation service returned an empty payload.");
                throw new InvalidOperationException("Translation service returned an empty payload.");
            }

            string[] translated = payload.TranslatedText;
            if (translated.Length != segments.Count)
            {
                AppLogger.Warn($"Translation response count mismatch. Expected={segments.Count}, Actual={translated.Length}.");
                throw new InvalidOperationException("Translation service response count does not match the request count.");
            }

            AppLogger.Info("TranslateCoreAsync completed successfully.");
            return segments.Select((segment, index) =>
                new TranslationResult(segment.Index, segment.SourceText, translated[index], segment.Bounds)).ToArray();
        }
    }

    private InvalidOperationException CreateTranslationConnectivityException(Uri baseUri, HttpRequestException exception)
    {
        if (baseUri == _sidecarManager.BaseUri)
        {
            return new InvalidOperationException(
                "无法连接本地翻译侧车服务。请确认已安装 Python 和 argostranslate，并检查日志中的 Sidecar 输出。",
                exception);
        }

        return new InvalidOperationException(
            $"无法连接在线翻译服务：{baseUri}",
            exception);
    }

    private sealed class TranslateRequest
    {
        [JsonPropertyName("q")]
        public string[] Q { get; set; } = [];

        [JsonPropertyName("source")]
        public string Source { get; set; } = "auto";

        [JsonPropertyName("target")]
        public string Target { get; set; } = "en";
    }

    private sealed class TranslateResponse
    {
        [JsonPropertyName("translatedText")]
        public string[]? TranslatedText { get; set; }
    }
}


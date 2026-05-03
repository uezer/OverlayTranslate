using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines.Translation;

public class DeepLTranslationEngine : ITranslationEngine
{
    public string Name => "DeepL";
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly bool _freeTier;

    public DeepLTranslationEngine(HttpClient httpClient, string apiKey, bool freeTier = true)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _freeTier = freeTier;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to)
    {
        var baseUrl = _freeTier
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text,
            ["target_lang"] = to.ToUpperInvariant(),
            ["source_lang"] = from == "auto" ? "" : from.ToUpperInvariant()
        });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var translated = doc.RootElement.GetProperty("translations")[0];

        return new TranslationResult
        {
            TranslatedText = translated.GetProperty("text").GetString() ?? "",
            SourceLanguage = translated.GetProperty("detected_source_language").GetString() ?? from,
            EngineName = Name
        };
    }

    public string[] GetSupportedLanguages() =>
        ["zh", "en", "ja", "ko", "fr", "de", "es", "ru", "auto"];
}

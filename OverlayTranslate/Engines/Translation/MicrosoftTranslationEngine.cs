using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines.Translation;

public class MicrosoftTranslationEngine : ITranslationEngine
{
    public string Name => "Microsoft";
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _region;
    private readonly string _endpoint;

    public MicrosoftTranslationEngine(HttpClient httpClient, string apiKey, string region = "", string endpoint = "https://api.cognitive.microsofttranslator.com")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _region = region;
        _endpoint = endpoint.TrimEnd('/');
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to)
    {
        var url = $"{_endpoint}/translate?api-version=3.0&to={to}";
        if (from != "auto")
            url += $"&from={from}";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
        if (!string.IsNullOrEmpty(_region))
            request.Headers.Add("Ocp-Apim-Subscription-Region", _region);

        var body = JsonSerializer.Serialize(new[] { new { text } });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement[0];
        var translated = result.GetProperty("translations")[0];

        return new TranslationResult
        {
            TranslatedText = translated.GetProperty("text").GetString() ?? "",
            SourceLanguage = result.GetProperty("detectedLanguage").GetProperty("language").GetString() ?? from,
            EngineName = Name
        };
    }

    public string[] GetSupportedLanguages() =>
        ["zh-Hans", "en", "ja", "ko", "fr", "de", "es", "ru", "pt", "it", "auto"];
}

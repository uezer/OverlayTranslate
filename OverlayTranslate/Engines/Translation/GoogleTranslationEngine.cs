using System.Net.Http;
using System.Text.Json;
using System.Threading;
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines.Translation;

public class GoogleTranslationEngine : ITranslationEngine
{
    public string Name => "Google";
    public bool IsAvailable => true;

    private readonly HttpClient _httpClient;

    public GoogleTranslationEngine(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken ct = default)
    {
        var sl = from == "auto" ? "auto" : from;
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={to}&dt=t&q={Uri.EscapeDataString(text)}";

        using var httpResponse = await _httpClient.GetAsync(url, ct);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(response);
        var sentences = doc.RootElement[0];

        var translatedText = string.Join("", sentences.EnumerateArray()
            .Select(s => s[0].GetString()));

        return new TranslationResult
        {
            TranslatedText = translatedText,
            SourceLanguage = from,
            EngineName = Name
        };
    }

    public string[] GetSupportedLanguages() =>
        ["zh-CN", "en", "ja", "ko", "fr", "de", "es", "ru", "auto"];
}

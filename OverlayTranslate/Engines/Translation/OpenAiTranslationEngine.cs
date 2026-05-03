using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines.Translation;

public class OpenAiTranslationEngine : ITranslationEngine
{
    public string Name => "OpenAI";
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiTranslationEngine(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = $"You are a translator. Translate the following text from {from} to {to}. Output only the translation, nothing else." },
                new { role = "user", content = text }
            }
        });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var translatedText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return new TranslationResult
        {
            TranslatedText = translatedText.Trim(),
            SourceLanguage = from,
            EngineName = Name
        };
    }

    public string[] GetSupportedLanguages() =>
        ["zh", "en", "ja", "ko", "fr", "de", "es", "ru", "auto"];
}

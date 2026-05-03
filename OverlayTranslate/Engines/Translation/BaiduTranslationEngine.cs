using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines.Translation;

public class BaiduTranslationEngine : ITranslationEngine
{
    public string Name => "Baidu";
    public bool IsAvailable => !string.IsNullOrEmpty(_appId) && !string.IsNullOrEmpty(_secret);

    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly string _secret;

    public BaiduTranslationEngine(HttpClient httpClient, string appId, string secret)
    {
        _httpClient = httpClient;
        _appId = appId;
        _secret = secret;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken ct = default)
    {
        var salt = Random.Shared.Next(10000).ToString();
        var sign = ComputeMd5($"{_appId}{text}{salt}{_secret}");

        var parameters = new Dictionary<string, string>
        {
            ["q"] = text,
            ["from"] = from == "auto" ? "auto" : from,
            ["to"] = to,
            ["appid"] = _appId,
            ["salt"] = salt,
            ["sign"] = sign
        };

        var response = await _httpClient.PostAsync(
            "https://fanyi-api.baidu.com/api/trans/vip/translate",
            new FormUrlEncodedContent(parameters), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("trans_result");

        var translatedText = string.Join("\n", results.EnumerateArray()
            .Select(r => r.GetProperty("dst").GetString()));

        return new TranslationResult
        {
            TranslatedText = translatedText,
            SourceLanguage = from,
            EngineName = Name
        };
    }

    private static string ComputeMd5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string[] GetSupportedLanguages() =>
        ["zh", "en", "ja", "ko", "fr", "de", "es", "auto"];
}

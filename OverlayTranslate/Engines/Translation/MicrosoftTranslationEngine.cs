using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines.Translation;

public partial class MicrosoftTranslationEngine : ITranslationEngine
{
    public string Name => "Microsoft";
    public bool IsAvailable => true;

    private readonly HttpClient _httpClient;

    // Bing 翻译网页抓取的认证参数
    private string _ig = "";
    private string _iid = "";
    private string _key = "";
    private string _token = "";
    private long _tokenTs;
    private long _tokenExpiryInterval = 3600000; // 默认 1 小时
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public MicrosoftTranslationEngine(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.bing.com/translator");
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);

        var fromLang = from == "auto" ? "auto-detect" : MapLanguageCode(from);
        var toLang = MapLanguageCode(to);

        for (int retry = 0; retry < 3; retry++)
        {
            ct.ThrowIfCancellationRequested();

            var form = new Dictionary<string, string>
            {
                ["fromLang"] = fromLang,
                ["to"] = toLang,
                ["text"] = text,
                ["token"] = _token,
                ["key"] = _key,
                ["tryFetchingGenderDebiasedTranslations"] = "true"
            };

            var url = $"https://www.bing.com/ttranslatev3?isVertical=1&IG={_ig}&IID={_iid}";
            var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(form), ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await RefreshTokenAsync(ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);

            if (json.Contains("ShowCaptcha"))
            {
                await RefreshTokenAsync(ct);
                continue;
            }

            using var doc = JsonDocument.Parse(json);
            var result = doc.RootElement[0];
            var translated = result.GetProperty("translations")[0];

            return new TranslationResult
            {
                TranslatedText = translated.GetProperty("text").GetString() ?? "",
                SourceLanguage = result.TryGetProperty("detectedLanguage", out var dl)
                    ? dl.GetProperty("language").GetString() ?? from : from,
                EngineName = Name
            };
        }

        throw new InvalidOperationException("Bing 翻译失败：重试次数用尽");
    }

    public string[] GetSupportedLanguages() =>
        ["zh", "zh-CN", "zh-Hans", "zh-Hant", "en", "ja", "ko", "fr", "de", "es", "ru", "pt", "it", "auto"];

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _tokenTs < _tokenExpiryInterval)
            return;
        await RefreshTokenAsync(ct);
    }

    private async Task RefreshTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            // 双重检查
            if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _tokenTs < _tokenExpiryInterval)
                return;

            var html = await _httpClient.GetStringAsync("https://www.bing.com/translator", ct);

            var igMatch = IgRegex().Match(html);
            if (igMatch.Success) _ig = igMatch.Groups[1].Value;

            var iidMatch = IidRegex().Match(html);
            if (iidMatch.Success) _iid = iidMatch.Groups[1].Value;

            var paramsMatch = ParamsRegex().Match(html);
            if (paramsMatch.Success)
            {
                var arr = JsonDocument.Parse(paramsMatch.Value);
                var root = arr.RootElement;
                _key = root[0].GetInt64().ToString();
                _token = root[1].GetString() ?? "";
                _tokenExpiryInterval = root[2].GetInt64();
                _tokenTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string MapLanguageCode(string code) => code switch
    {
        "zh" or "zh-CN" or "zh-CHS" => "zh-Hans",
        "zh-TW" or "zh-CHT" => "zh-Hant",
        _ => code
    };

    [GeneratedRegex(@"IG:""([^""]+)""")]
    private static partial Regex IgRegex();

    [GeneratedRegex(@"data-iid=""([^""]+)""")]
    private static partial Regex IidRegex();

    [GeneratedRegex(@"params_AbusePreventionHelper\s?=\s?([^\]]+\])")]
    private static partial Regex ParamsRegex();
}

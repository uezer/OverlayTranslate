using System.Net;
using System.Net.Http;
using System.Text.Json;
using OverlayTranslate.Engines.Translation;

namespace OverlayTranslate.Tests;

public class MicrosoftTranslationTests
{
    private const string TranslatorPageHtml = """
        <html>
        <script>var defined = true;</script>
        <script>
        IG:"A1B2C3D4E5F6";
        var defined = true;
        data-iid="translator.5023";
        params_AbusePreventionHelper = [1234567890, "abc123token", 3600000];
        </script>
        </html>
        """;

    private const string TranslateResponseJson = """
        [{"detectedLanguage":{"language":"en","score":0.9},"translations":[{"text":"你好世界","to":"zh-Hans"}]}]
        """;

    [Fact]
    public void HasCorrectName()
    {
        var engine = new MicrosoftTranslationEngine(new HttpClient());
        Assert.Equal("Microsoft", engine.Name);
    }

    [Fact]
    public void IsAlwaysAvailable()
    {
        var engine = new MicrosoftTranslationEngine(new HttpClient());
        Assert.True(engine.IsAvailable);
    }

    [Fact]
    public void GetSupportedLanguages_ContainsExpectedLanguages()
    {
        var engine = new MicrosoftTranslationEngine(new HttpClient());
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("zh", languages);
        Assert.Contains("zh-CN", languages);
        Assert.Contains("en", languages);
        Assert.Contains("ja", languages);
        Assert.Contains("auto", languages);
    }

    [Fact]
    public async Task TranslateAsync_ParsesResponse()
    {
        var handler = new BingMockHandler(TranslatorPageHtml, TranslateResponseJson);
        var httpClient = new HttpClient(handler);
        var engine = new MicrosoftTranslationEngine(httpClient);

        var result = await engine.TranslateAsync("Hello World", "en", "zh");

        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("Microsoft", result.EngineName);
    }

    [Fact]
    public async Task TranslateAsync_AutoDetect()
    {
        var handler = new BingMockHandler(TranslatorPageHtml, TranslateResponseJson);
        var httpClient = new HttpClient(handler);
        var engine = new MicrosoftTranslationEngine(httpClient);

        var result = await engine.TranslateAsync("Hello World", "auto", "zh");

        Assert.Equal("你好世界", result.TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_RefreshesTokenOn401()
    {
        var handler = new BingRetryHandler(TranslatorPageHtml, TranslateResponseJson);
        var httpClient = new HttpClient(handler);
        var engine = new MicrosoftTranslationEngine(httpClient);

        var result = await engine.TranslateAsync("Hello", "en", "zh");

        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal(2, handler.TranslateCallCount); // 第一次 401，第二次成功
    }

    /// <summary>
    /// Mock handler: 第一个请求返回 translator 页面，第二个请求返回翻译结果。
    /// </summary>
    private class BingMockHandler : HttpMessageHandler
    {
        private readonly string _pageHtml;
        private readonly string _translateJson;
        private int _callCount;

        public BingMockHandler(string pageHtml, string translateJson)
        {
            _pageHtml = pageHtml;
            _translateJson = translateJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            var url = request.RequestUri!.ToString();

            if (url.Contains("/translator"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_pageHtml)
                });
            }

            if (url.Contains("/ttranslatev3"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_translateJson)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>
    /// Mock handler: 翻译请求第一次返回 401，第二次返回成功。
    /// </summary>
    private class BingRetryHandler : HttpMessageHandler
    {
        private readonly string _pageHtml;
        private readonly string _translateJson;
        private int _translateCallCount;
        public int TranslateCallCount => _translateCallCount;

        public BingRetryHandler(string pageHtml, string translateJson)
        {
            _pageHtml = pageHtml;
            _translateJson = translateJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/translator"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_pageHtml)
                });
            }

            if (url.Contains("/ttranslatev3"))
            {
                _translateCallCount++;
                if (_translateCallCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_translateJson)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

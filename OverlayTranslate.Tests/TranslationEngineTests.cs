using System.Net;
using System.Net.Http;
using System.Text.Json;
using OverlayTranslate.Engines.Translation;
using OverlayTranslate.Models;

namespace OverlayTranslate.Tests;

public class TranslationEngineTests
{
    private static HttpClient CreateMockHttpClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseContent, statusCode);
        return new HttpClient(handler);
    }

    [Fact]
    public void DeepL_HasCorrectName()
    {
        var engine = new DeepLTranslationEngine(new HttpClient(), "test-key");
        Assert.Equal("DeepL", engine.Name);
    }

    [Fact]
    public void DeepL_IsAvailable_WhenApiKeyProvided()
    {
        var engine = new DeepLTranslationEngine(new HttpClient(), "test-key");
        Assert.True(engine.IsAvailable);
    }

    [Fact]
    public void DeepL_IsNotAvailable_WhenApiKeyEmpty()
    {
        var engine = new DeepLTranslationEngine(new HttpClient(), "");
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void DeepL_GetSupportedLanguages_ContainsExpectedLanguages()
    {
        var engine = new DeepLTranslationEngine(new HttpClient(), "key");
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("zh", languages);
        Assert.Contains("en", languages);
        Assert.Contains("ja", languages);
        Assert.Contains("auto", languages);
    }

    [Fact]
    public void Google_HasCorrectName()
    {
        var engine = new GoogleTranslationEngine(new HttpClient());
        Assert.Equal("Google", engine.Name);
    }

    [Fact]
    public void Google_IsAlwaysAvailable()
    {
        var engine = new GoogleTranslationEngine(new HttpClient());
        Assert.True(engine.IsAvailable);
    }

    [Fact]
    public void Google_GetSupportedLanguages_ContainsExpectedLanguages()
    {
        var engine = new GoogleTranslationEngine(new HttpClient());
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("zh-CN", languages);
        Assert.Contains("en", languages);
        Assert.Contains("auto", languages);
    }

    [Fact]
    public void Baidu_HasCorrectName()
    {
        var engine = new BaiduTranslationEngine(new HttpClient(), "app-id", "secret");
        Assert.Equal("Baidu", engine.Name);
    }

    [Fact]
    public void Baidu_IsAvailable_WhenCredentialsProvided()
    {
        var engine = new BaiduTranslationEngine(new HttpClient(), "app-id", "secret");
        Assert.True(engine.IsAvailable);
    }

    [Fact]
    public void Baidu_IsNotAvailable_WhenAppIdEmpty()
    {
        var engine = new BaiduTranslationEngine(new HttpClient(), "", "secret");
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void Baidu_IsNotAvailable_WhenSecretEmpty()
    {
        var engine = new BaiduTranslationEngine(new HttpClient(), "app-id", "");
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void Baidu_GetSupportedLanguages_ContainsExpectedLanguages()
    {
        var engine = new BaiduTranslationEngine(new HttpClient(), "id", "secret");
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("zh", languages);
        Assert.Contains("en", languages);
        Assert.Contains("auto", languages);
    }

    [Fact]
    public void OpenAI_HasCorrectName()
    {
        var engine = new OpenAiTranslationEngine(new HttpClient(), "api-key");
        Assert.Equal("OpenAI", engine.Name);
    }

    [Fact]
    public void OpenAI_IsAvailable_WhenApiKeyProvided()
    {
        var engine = new OpenAiTranslationEngine(new HttpClient(), "api-key");
        Assert.True(engine.IsAvailable);
    }

    [Fact]
    public void OpenAI_IsNotAvailable_WhenApiKeyEmpty()
    {
        var engine = new OpenAiTranslationEngine(new HttpClient(), "");
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void OpenAI_GetSupportedLanguages_ContainsExpectedLanguages()
    {
        var engine = new OpenAiTranslationEngine(new HttpClient(), "key");
        var languages = engine.GetSupportedLanguages();

        Assert.Contains("zh", languages);
        Assert.Contains("en", languages);
        Assert.Contains("ja", languages);
        Assert.Contains("auto", languages);
    }

    [Fact]
    public async Task Google_TranslateAsync_ParsesResponse()
    {
        // Google returns [[["translated text","original text",null,null,10]]]
        var responseJson = "[[[\"你好世界\",\"Hello World\",null,null,10]]]";
        var httpClient = CreateMockHttpClient(responseJson);
        var engine = new GoogleTranslationEngine(httpClient);

        var result = await engine.TranslateAsync("Hello World", "en", "zh-CN");

        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal("Google", result.EngineName);
    }

    [Fact]
    public async Task DeepL_TranslateAsync_ParsesResponse()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            translations = new[]
            {
                new { text = "你好世界", detected_source_language = "EN" }
            }
        });
        var httpClient = CreateMockHttpClient(responseJson);
        var engine = new DeepLTranslationEngine(httpClient, "test-key");

        var result = await engine.TranslateAsync("Hello World", "en", "zh");

        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal("EN", result.SourceLanguage);
        Assert.Equal("DeepL", result.EngineName);
    }

    [Fact]
    public async Task Baidu_TranslateAsync_ParsesResponse()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            trans_result = new[]
            {
                new { src = "Hello", dst = "你好" }
            }
        });
        var httpClient = CreateMockHttpClient(responseJson);
        var engine = new BaiduTranslationEngine(httpClient, "app-id", "secret");

        var result = await engine.TranslateAsync("Hello", "en", "zh");

        Assert.Equal("你好", result.TranslatedText);
        Assert.Equal("Baidu", result.EngineName);
    }

    [Fact]
    public async Task OpenAI_TranslateAsync_ParsesResponse()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "你好世界"
                    }
                }
            }
        });
        var httpClient = CreateMockHttpClient(responseJson);
        var engine = new OpenAiTranslationEngine(httpClient, "api-key");

        var result = await engine.TranslateAsync("Hello World", "en", "zh");

        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal("OpenAI", result.EngineName);
    }

    [Fact]
    public async Task Baidu_TranslateAsync_MultilineResult()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            trans_result = new[]
            {
                new { src = "Hello", dst = "你好" },
                new { src = "World", dst = "世界" }
            }
        });
        var httpClient = CreateMockHttpClient(responseJson);
        var engine = new BaiduTranslationEngine(httpClient, "app-id", "secret");

        var result = await engine.TranslateAsync("Hello\nWorld", "en", "zh");

        Assert.Equal("你好\n世界", result.TranslatedText);
    }

    /// <summary>
    /// Simple mock HTTP message handler for testing.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(response);
        }
    }
}

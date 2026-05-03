using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using OverlayTranslate.Models;
using Serilog;

namespace OverlayTranslate.Engines.Ocr;

public class RemoteOcrEngine : IOcrEngine
{
    public string Name => "RemoteOCR";
    public bool IsAvailable => !string.IsNullOrEmpty(_endpoint);

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;

    public RemoteOcrEngine(HttpClient httpClient, string endpoint, string apiKey = "")
    {
        _httpClient = httpClient;
        _endpoint = endpoint;
        _apiKey = apiKey;
    }

    public async Task<OcrResult> RecognizeAsync(byte[] imageData, string language = "auto")
    {
        if (string.IsNullOrEmpty(_endpoint))
            throw new InvalidOperationException("远程 OCR 端点未配置");

        var request = new
        {
            image = Convert.ToBase64String(imageData),
            language
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        httpRequest.Content = JsonContent.Create(request);
        if (!string.IsNullOrEmpty(_apiKey))
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var textBlocks = new List<TextBlock>();
        if (root.TryGetProperty("textBlocks", out var blocks))
        {
            foreach (var block in blocks.EnumerateArray())
            {
                textBlocks.Add(new TextBlock
                {
                    Text = block.GetProperty("text").GetString() ?? "",
                    Confidence = block.TryGetProperty("confidence", out var c) ? c.GetSingle() : 1.0f
                });
            }
        }

        return new OcrResult
        {
            TextBlocks = textBlocks,
            FullText = string.Join("\n", textBlocks.Select(b => b.Text)),
            Language = language
        };
    }

    public string[] GetSupportedLanguages() => ["auto"];
}

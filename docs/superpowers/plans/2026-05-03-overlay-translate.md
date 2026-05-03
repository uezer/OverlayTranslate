# OverlayTranslate 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 构建一款 Windows 桌面截图翻译工具，通过全屏覆盖 + 框选区域实现 OCR + 翻译的原位覆盖显示。

**架构：** WPF 覆盖层窗口（透明全屏）+ 插件化 OCR/翻译引擎 + OpenCV 图像处理 + Python.NET 互操作。通过 DI 容器管理服务和引擎，JSON 配置持久化用户设置。

**技术栈：** .NET 10.0, WPF, PaddleOCRSharp, OpenCvSharp4, H.NotifyIcon.Wpf, pythonnet, Serilog, MS.Extensions.DI

---

## 文件结构

### Models/
- `Models/OcrResult.cs` — OCR 识别结果，包含文字块列表
- `Models/TextBlock.cs` — 单个文字块（文本、边界框、置信度、角度）
- `Models/TranslationResult.cs` — 翻译结果
- `Models/AppSettings.cs` — 应用配置模型（OCR/翻译引擎配置、热键、语言）

### Engines/
- `Engines/IOcrEngine.cs` — OCR 引擎接口
- `Engines/ITranslationEngine.cs` — 翻译引擎接口
- `Engines/Ocr/PaddleOcrEngine.cs` — PaddleOCR 本地引擎
- `Engines/Ocr/RemoteOcrEngine.cs` — 远程 OCR API 引擎
- `Engines/Translation/DeepLTranslationEngine.cs` — DeepL 翻译
- `Engines/Translation/GoogleTranslationEngine.cs` — Google 翻译
- `Engines/Translation/BaiduTranslationEngine.cs` — 百度翻译
- `Engines/Translation/OpenAiTranslationEngine.cs` — OpenAI 翻译

### Services/
- `Services/ScreenshotService.cs` — 屏幕截图服务
- `Services/ImageProcessor.cs` — 图像处理（背景色采样、原文覆盖）
- `Services/TextRenderer.cs` — 文字渲染（译文回绘）
- `Services/StyleAnalyzer.cs` — 原文样式分析

### Controls/
- `Controls/SelectionCanvas.cs` — 选区绘制控件
- `Controls/MaskLayer.cs` — 遮罩层控件
- `Controls/FloatingToolbar.xaml` + `.cs` — 浮动工具栏

### Windows/
- `Windows/OverlayWindow.xaml` + `.cs` — 覆盖层主窗口（状态机）

### Infrastructure/
- `Infrastructure/HotkeyManager.cs` — 全局热键管理
- `Infrastructure/ConfigManager.cs` — JSON 配置管理
- `Infrastructure/TrayIconManager.cs` — 系统托盘管理

### Python/
- `Python/PythonRuntime.cs` — Python 引擎生命周期管理
- `Python/PythonBridge.cs` — C# ↔ Python 调用桥接

### 入口
- `App.xaml` + `App.xaml.cs` — 应用入口，DI 注册，生命周期管理
- `MainWindow.xaml` + `MainWindow.xaml.cs` — 托盘宿主窗口（隐藏）

---

## 任务 1：项目配置与基础设施

**文件：**
- 修改：`OverlayTranslate/OverlayTranslate.csproj`
- 创建：`OverlayTranslate/Infrastructure/ConfigManager.cs`
- 创建：`OverlayTranslate/Models/AppSettings.cs`
- 创建：`OverlayTranslate/Config/appsettings.json`
- 创建：`OverlayTranslate/App.xaml.cs`

- [ ] **步骤 1：添加 NuGet 依赖**

```xml
<!-- OverlayTranslate.csproj 中添加 -->
<ItemGroup>
  <PackageReference Include="Serilog" Version="4.2.0" />
  <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
  <PackageReference Include="pythonnet" Version="3.0.5" />
</ItemGroup>
```

- [ ] **步骤 2：创建 AppSettings 模型**

```csharp
// Models/AppSettings.cs
namespace OverlayTranslate.Models;

public class AppSettings
{
    public OcrSettings Ocr { get; set; } = new();
    public TranslationSettings Translation { get; set; } = new();
    public HotkeySettings Hotkey { get; set; } = new();
    public LanguageSettings Language { get; set; } = new();
    public PythonSettings Python { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

public class OcrSettings
{
    public string ActiveEngine { get; set; } = "PaddleOCR";
    public string? FallbackEngine { get; set; }
    public string Strategy { get; set; } = "LocalFirst";
    public Dictionary<string, Dictionary<string, string>> Engines { get; set; } = new();
}

public class TranslationSettings
{
    public string ActiveEngine { get; set; } = "DeepL";
    public string? FallbackEngine { get; set; }
    public string Strategy { get; set; } = "LocalFirst";
    public Dictionary<string, Dictionary<string, string>> Engines { get; set; } = new();
}

public class HotkeySettings
{
    public string[] Modifiers { get; set; } = ["Ctrl", "Shift"];
    public string Key { get; set; } = "T";
}

public class LanguageSettings
{
    public string Source { get; set; } = "auto";
    public string Target { get; set; } = "zh-CN";
}

public class PythonSettings
{
    public string RuntimePath { get; set; } = "";
}

public class LoggingSettings
{
    public string Level { get; set; } = "Information";
    public string File { get; set; } = "logs/app.log";
}
```

- [ ] **步骤 3：创建 ConfigManager**

```csharp
// Infrastructure/ConfigManager.cs
using System.Text.Json;
using OverlayTranslate.Models;

namespace OverlayTranslate.Infrastructure;

public class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "appsettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        else
        {
            Save(); // 写入默认配置
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
```

- [ ] **步骤 4：创建默认 appsettings.json**

```json
{
  "ocr": {
    "activeEngine": "PaddleOCR",
    "fallbackEngine": null,
    "strategy": "LocalFirst",
    "engines": {
      "PaddleOCR": { "modelPath": "inference/" },
      "RemoteOCR": { "endpoint": "http://localhost:1224/api/ocr", "apiKey": "" }
    }
  },
  "translation": {
    "activeEngine": "DeepL",
    "fallbackEngine": "Google",
    "strategy": "LocalFirst",
    "engines": {
      "DeepL": { "apiKey": "", "freeTier": "true" },
      "Google": { "endpoint": "free" },
      "Baidu": { "appId": "", "secret": "" },
      "OpenAI": { "apiKey": "", "model": "gpt-4o-mini" }
    }
  },
  "hotkey": { "modifiers": ["Ctrl", "Shift"], "key": "T" },
  "language": { "source": "auto", "target": "zh-CN" },
  "python": { "runtimePath": "" },
  "logging": { "level": "Information", "file": "logs/app.log" }
}
```

- [ ] **步骤 5：配置 App.xaml.cs 入口**

```csharp
// App.xaml.cs
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Infrastructure;
using Serilog;

namespace OverlayTranslate;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 加载配置
        var configManager = new ConfigManager();
        configManager.Load();

        // 配置 Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(configManager.Settings.Logging.File, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // 配置 DI
        var services = new ServiceCollection();
        ConfigureServices(services, configManager);
        Services = services.BuildServiceProvider();

        // 启动托盘（后续任务实现）
        // Services.GetRequiredService<TrayIconManager>();
    }

    private void ConfigureServices(IServiceCollection services, ConfigManager configManager)
    {
        services.AddSingleton(configManager);
        services.AddHttpClient();
        // 后续任务添加更多服务注册
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
```

- [ ] **步骤 6：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功，无错误

- [ ] **步骤 7：Commit**

```bash
git add OverlayTranslate/OverlayTranslate.csproj OverlayTranslate/App.xaml.cs OverlayTranslate/Infrastructure/ConfigManager.cs OverlayTranslate/Models/AppSettings.cs OverlayTranslate/Config/appsettings.json
git commit -m "feat: 项目基础设施 - 配置管理、DI、日志"
```

---

## 任务 2：引擎接口与数据模型

**文件：**
- 创建：`OverlayTranslate/Engines/IOcrEngine.cs`
- 创建：`OverlayTranslate/Engines/ITranslationEngine.cs`
- 创建：`OverlayTranslate/Models/OcrResult.cs`
- 创建：`OverlayTranslate/Models/TextBlock.cs`
- 创建：`OverlayTranslate/Models/TranslationResult.cs`

- [ ] **步骤 1：创建 TextBlock 模型**

```csharp
// Models/TextBlock.cs
using System.Windows;

namespace OverlayTranslate.Models;

public class TextBlock
{
    public string Text { get; set; } = "";
    public Rect BoundingBox { get; set; }
    public float Confidence { get; set; }
    public float Angle { get; set; }
}
```

- [ ] **步骤 2：创建 OcrResult 模型**

```csharp
// Models/OcrResult.cs
namespace OverlayTranslate.Models;

public class OcrResult
{
    public List<TextBlock> TextBlocks { get; set; } = [];
    public string FullText { get; set; } = "";
    public string Language { get; set; } = "";
}
```

- [ ] **步骤 3：创建 TranslationResult 模型**

```csharp
// Models/TranslationResult.cs
namespace OverlayTranslate.Models;

public class TranslationResult
{
    public string TranslatedText { get; set; } = "";
    public string SourceLanguage { get; set; } = "";
    public string EngineName { get; set; } = "";
}
```

- [ ] **步骤 4：创建 IOcrEngine 接口**

```csharp
// Engines/IOcrEngine.cs
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines;

public interface IOcrEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<OcrResult> RecognizeAsync(byte[] imageData, string language = "auto");
    string[] GetSupportedLanguages();
}
```

- [ ] **步骤 5：创建 ITranslationEngine 接口**

```csharp
// Engines/ITranslationEngine.cs
using OverlayTranslate.Models;

namespace OverlayTranslate.Engines;

public interface ITranslationEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<TranslationResult> TranslateAsync(string text, string from, string to);
    string[] GetSupportedLanguages();
}
```

- [ ] **步骤 6：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 7：Commit**

```bash
git add OverlayTranslate/Engines/IOcrEngine.cs OverlayTranslate/Engines/ITranslationEngine.cs OverlayTranslate/Models/OcrResult.cs OverlayTranslate/Models/TextBlock.cs OverlayTranslate/Models/TranslationResult.cs
git commit -m "feat: 引擎接口和数据模型定义"
```

---

## 任务 3：PaddleOCR 引擎实现

**文件：**
- 创建：`OverlayTranslate/Engines/Ocr/PaddleOcrEngine.cs`
- 修改：`OverlayTranslate/App.xaml.cs`（注册引擎）

- [ ] **步骤 1：实现 PaddleOcrEngine**

```csharp
// Engines/Ocr/PaddleOcrEngine.cs
using System.Windows;
using OverlayTranslate.Models;
using PaddleOCRSharp;
using Serilog;

namespace OverlayTranslate.Engines.Ocr;

public class PaddleOcrEngine : IOcrEngine
{
    public string Name => "PaddleOCR";
    public bool IsAvailable => _engine != null;

    private PaddleOCREngine? _engine;
    private readonly string _modelPath;

    public PaddleOcrEngine(string modelPath = "inference/")
    {
        _modelPath = modelPath;
        try
        {
            var config = new PaddleOCREngineConfig(_modelPath);
            _engine = new PaddleOCREngine(config);
            Log.Information("PaddleOCR 引擎初始化成功");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PaddleOCR 引擎初始化失败");
            _engine = null;
        }
    }

    public async Task<OcrResult> RecognizeAsync(byte[] imageData, string language = "auto")
    {
        if (_engine == null)
            throw new InvalidOperationException("PaddleOCR 引擎未初始化");

        return await Task.Run(() =>
        {
            var result = _engine.DetectText(imageData);
            var textBlocks = new List<TextBlock>();

            foreach (var block in result.TextBlocks)
            {
                textBlocks.Add(new TextBlock
                {
                    Text = block.Text,
                    BoundingBox = new Rect(
                        block.Box.Left, block.Box.Top,
                        block.Box.Right - block.Box.Left,
                        block.Box.Bottom - block.Box.Top),
                    Confidence = block.Confidence,
                    Angle = 0
                });
            }

            return new OcrResult
            {
                TextBlocks = textBlocks,
                FullText = string.Join("\n", textBlocks.Select(b => b.Text)),
                Language = language
            };
        });
    }

    public string[] GetSupportedLanguages() =>
        ["ch", "en", "japan", "korean", "fr", "german", "auto"];
}
```

- [ ] **步骤 2：在 DI 中注册 OCR 引擎**

```csharp
// App.xaml.cs 的 ConfigureServices 方法中添加：
services.AddSingleton<IOcrEngine>(sp =>
{
    var config = sp.GetRequiredService<ConfigManager>();
    var modelPath = config.Settings.Ocr.Engines
        .GetValueOrDefault("PaddleOCR")
        ?.GetValueOrDefault("modelPath") ?? "inference/";
    return new PaddleOcrEngine(modelPath);
});
```

- [ ] **步骤 3：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Engines/Ocr/PaddleOcrEngine.cs OverlayTranslate/App.xaml.cs
git commit -m "feat: PaddleOCR 引擎实现"
```

---

## 任务 4：远程 OCR 引擎实现

**文件：**
- 创建：`OverlayTranslate/Engines/Ocr/RemoteOcrEngine.cs`

- [ ] **步骤 1：实现 RemoteOcrEngine**

```csharp
// Engines/Ocr/RemoteOcrEngine.cs
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
```

- [ ] **步骤 2：在 DI 中注册远程 OCR 引擎**

```csharp
// App.xaml.cs 的 ConfigureServices 方法中添加：
services.AddSingleton<RemoteOcrEngine>(sp =>
{
    var config = sp.GetRequiredService<ConfigManager>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var endpoint = config.Settings.Ocr.Engines
        .GetValueOrDefault("RemoteOCR")
        ?.GetValueOrDefault("endpoint") ?? "";
    var apiKey = config.Settings.Ocr.Engines
        .GetValueOrDefault("RemoteOCR")
        ?.GetValueOrDefault("apiKey") ?? "";
    return new RemoteOcrEngine(http, endpoint, apiKey);
});
```

- [ ] **步骤 3：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/Engines/Ocr/RemoteOcrEngine.cs OverlayTranslate/App.xaml.cs
git commit -m "feat: 远程 OCR 引擎实现"
```

---

## 任务 5：翻译引擎实现

**文件：**
- 创建：`OverlayTranslate/Engines/Translation/DeepLTranslationEngine.cs`
- 创建：`OverlayTranslate/Engines/Translation/GoogleTranslationEngine.cs`
- 创建：`OverlayTranslate/Engines/Translation/BaiduTranslationEngine.cs`
- 创建：`OverlayTranslate/Engines/Translation/OpenAiTranslationEngine.cs`

- [ ] **步骤 1：实现 DeepLTranslationEngine**

```csharp
// Engines/Translation/DeepLTranslationEngine.cs
using System.Net.Http.Json;
using System.Text.Json;
using OverlayTranslate.Models;
using Serilog;

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
        var doc = JsonDocument.Parse(json);
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
```

- [ ] **步骤 2：实现 GoogleTranslationEngine**

```csharp
// Engines/Translation/GoogleTranslationEngine.cs
using System.Net.Http.Json;
using System.Text.Json;
using OverlayTranslate.Models;
using Serilog;

namespace OverlayTranslate.Engines.Translation;

public class GoogleTranslationEngine : ITranslationEngine
{
    public string Name => "Google";
    public bool IsAvailable => true; // 使用免费端点

    private readonly HttpClient _httpClient;

    public GoogleTranslationEngine(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to)
    {
        var sl = from == "auto" ? "auto" : from;
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={to}&dt=t&q={Uri.EscapeDataString(text)}";

        var response = await _httpClient.GetStringAsync(url);
        var doc = JsonDocument.Parse(response);
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
```

- [ ] **步骤 3：实现 BaiduTranslationEngine**

```csharp
// Engines/Translation/BaiduTranslationEngine.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OverlayTranslate.Models;
using Serilog;

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

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to)
    {
        var salt = Random.Shared.Next(10000).ToString();
        var sign = ComputeMd5($"{_appId}{text}{salt}{_secret}");

        var url = "https://fanyi-api.baidu.com/api/trans/vip/translate";
        var parameters = new Dictionary<string, string>
        {
            ["q"] = text,
            ["from"] = from == "auto" ? "auto" : from,
            ["to"] = to,
            ["appid"] = _appId,
            ["salt"] = salt,
            ["sign"] = sign
        };

        var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(parameters));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
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
```

- [ ] **步骤 4：实现 OpenAiTranslationEngine**

```csharp
// Engines/Translation/OpenAiTranslationEngine.cs
using System.Net.Http.Headers;
using System.Text.Json;
using OverlayTranslate.Models;
using Serilog;

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
        var doc = JsonDocument.Parse(json);
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
```

- [ ] **步骤 5：在 DI 中注册翻译引擎**

```csharp
// App.xaml.cs 的 ConfigureServices 方法中添加：
services.AddSingleton<DeepLTranslationEngine>(sp =>
{
    var config = sp.GetRequiredService<ConfigManager>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var cfg = config.Settings.Translation.Engines.GetValueOrDefault("DeepL");
    return new DeepLTranslationEngine(http, cfg?.GetValueOrDefault("apiKey") ?? "", cfg?.GetValueOrDefault("freeTier") == "true");
});

services.AddSingleton<GoogleTranslationEngine>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    return new GoogleTranslationEngine(http);
});

services.AddSingleton<BaiduTranslationEngine>(sp =>
{
    var config = sp.GetRequiredService<ConfigManager>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var cfg = config.Settings.Translation.Engines.GetValueOrDefault("Baidu");
    return new BaiduTranslationEngine(http, cfg?.GetValueOrDefault("appId") ?? "", cfg?.GetValueOrDefault("secret") ?? "");
});

services.AddSingleton<OpenAiTranslationEngine>(sp =>
{
    var config = sp.GetRequiredService<ConfigManager>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var cfg = config.Settings.Translation.Engines.GetValueOrDefault("OpenAI");
    return new OpenAiTranslationEngine(http, cfg?.GetValueOrDefault("apiKey") ?? "", cfg?.GetValueOrDefault("model") ?? "gpt-4o-mini");
});
```

- [ ] **步骤 6：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 7：Commit**

```bash
git add OverlayTranslate/Engines/Translation/
git add OverlayTranslate/App.xaml.cs
git commit -m "feat: 翻译引擎实现 - DeepL, Google, Baidu, OpenAI"
```

---

## 任务 6：截图与图像处理服务

**文件：**
- 创建：`OverlayTranslate/Services/ScreenshotService.cs`
- 创建：`OverlayTranslate/Services/ImageProcessor.cs`
- 创建：`OverlayTranslate/Services/StyleAnalyzer.cs`
- 创建：`OverlayTranslate/Services/TextRenderer.cs`

- [ ] **步骤 1：实现 ScreenshotService**

```csharp
// Services/ScreenshotService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;

namespace OverlayTranslate.Services;

public class ScreenshotService
{
    public byte[] CaptureFullScreen()
    {
        var bounds = SystemParameters.WorkArea;
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, new Size(width, height));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public byte[] CaptureRegion(Rect region)
    {
        var width = (int)region.Width;
        var height = (int)region.Height;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)region.Left, (int)region.Top, 0, 0, new Size(width, height));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
```

- [ ] **步骤 2：实现 ImageProcessor**

```csharp
// Services/ImageProcessor.cs
using System.Windows;
using OpenCvSharp;

namespace OverlayTranslate.Services;

public class ImageProcessor
{
    /// <summary>
    /// 采样文字块周围的背景色
    /// </summary>
    public Scalar SampleBackgroundColor(byte[] imageData, Rect textRegion, int sampleMargin = 5)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var x = Math.Max(0, (int)textRegion.X - sampleMargin);
        var y = Math.Max(0, (int)textRegion.Y - sampleMargin);
        var w = Math.Min(src.Width - x, (int)textRegion.Width + sampleMargin * 2);
        var h = Math.Min(src.Height - y, (int)textRegion.Height + sampleMargin * 2);

        // 采样四周边缘像素
        using var border = src[new Rect(x, y, w, h)];
        var mean = Cv2.Mean(border);
        return mean;
    }

    /// <summary>
    /// 用指定颜色填充文字块区域（覆盖原文）
    /// </summary>
    public byte[] FillRegion(byte[] imageData, Rect region, Scalar color)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var rect = new OpenCvSharp.Rect(
            Math.Max(0, (int)region.X),
            Math.Max(0, (int)region.Y),
            Math.Min(src.Width - (int)region.X, (int)region.Width),
            Math.Min(src.Height - (int)region.Y, (int)region.Height));

        Cv2.Rectangle(src, rect, color, -1);

        Cv2.ImEncode(".png", src, out var buf);
        return buf.ToArray();
    }

    /// <summary>
    /// 使用 OpenCV inpaint 修复文字区域
    /// </summary>
    public byte[] InpaintRegion(byte[] imageData, Rect region)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        using var mask = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        using var dst = new Mat();

        var rect = new OpenCvSharp.Rect(
            Math.Max(0, (int)region.X),
            Math.Max(0, (int)region.Y),
            Math.Min(src.Width - (int)region.X, (int)region.Width),
            Math.Min(src.Height - (int)region.Y, (int)region.Height));

        Cv2.Rectangle(mask, rect, Scalar.All(255), -1);
        Cv2.Inpaint(src, mask, dst, 3, InpaintMethod.Telea);

        Cv2.ImEncode(".png", dst, out var buf);
        return buf.ToArray();
    }
}
```

- [ ] **步骤 3：实现 StyleAnalyzer**

```csharp
// Services/StyleAnalyzer.cs
using System.Windows;
using System.Windows.Media;

namespace OverlayTranslate.Services;

public class TextStyleInfo
{
    public double FontSize { get; set; }
    public Color TextColor { get; set; }
    public bool IsBold { get; set; }
    public double RegionWidth { get; set; }
    public double RegionHeight { get; set; }
}

public class StyleAnalyzer
{
    /// <summary>
    /// 根据文字块边界框估算字体大小
    /// </summary>
    public TextStyleInfo Analyze(Rect boundingBox, string text)
    {
        // 简单估算：字高 ≈ 边界框高度，字号 ≈ 字高 * 0.75
        var estimatedFontSize = boundingBox.Height * 0.75;

        return new TextStyleInfo
        {
            FontSize = Math.Max(8, Math.Min(72, estimatedFontSize)),
            TextColor = Colors.Black,
            IsBold = false,
            RegionWidth = boundingBox.Width,
            RegionHeight = boundingBox.Height
        };
    }

    /// <summary>
    /// 根据译文长度调整字号，确保不超出边界框
    /// </summary>
    public double AdjustFontSize(string translatedText, TextStyleInfo originalStyle)
    {
        var originalLength = translatedText.Length;
        if (originalLength == 0) return originalStyle.FontSize;

        // 估算每个字符的宽度
        var charWidth = originalStyle.FontSize * 0.6;
        var totalWidth = charWidth * originalLength;

        if (totalWidth <= originalStyle.RegionWidth)
            return originalStyle.FontSize;

        // 按比例缩小
        var ratio = originalStyle.RegionWidth / totalWidth;
        return Math.Max(8, originalStyle.FontSize * ratio);
    }
}
```

- [ ] **步骤 4：实现 TextRenderer**

```csharp
// Services/TextRenderer.cs
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OverlayTranslate.Services;

public class TextRenderer
{
    /// <summary>
    /// 在指定位置渲染译文，返回合成后的图像
    /// </summary>
    public BitmapSource RenderTranslatedText(
        byte[] backgroundImage,
        string translatedText,
        Rect region,
        TextStyleInfo style)
    {
        var bgImage = new BitmapImage();
        using (var stream = new MemoryStream(backgroundImage))
        {
            bgImage.BeginInit();
            bgImage.CacheOption = BitmapCacheOption.OnLoad;
            bgImage.StreamSource = stream;
            bgImage.EndInit();
            bgImage.Freeze();
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 绘制背景图
            dc.DrawImage(bgImage, new Rect(0, 0, bgImage.PixelWidth, bgImage.PixelHeight));

            // 绘制译文
            var adjustedSize = new StyleAnalyzer().AdjustFontSize(translatedText, style);
            var typeface = new Typeface("Microsoft YaHei");
            var formattedText = new FormattedText(
                translatedText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                adjustedSize,
                new SolidColorBrush(style.TextColor),
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);

            // 居中绘制在区域内
            var x = region.X + (region.Width - formattedText.Width) / 2;
            var y = region.Y + (region.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(x, y));
        }

        var renderTarget = new RenderTargetBitmap(
            bgImage.PixelWidth, bgImage.PixelHeight,
            bgImage.DpiX, bgImage.DpiY,
            PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        return renderTarget;
    }
}
```

- [ ] **步骤 5：在 DI 中注册服务**

```csharp
// App.xaml.cs 的 ConfigureServices 方法中添加：
services.AddSingleton<ScreenshotService>();
services.AddSingleton<ImageProcessor>();
services.AddSingleton<StyleAnalyzer>();
services.AddSingleton<TextRenderer>();
```

- [ ] **步骤 6：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 7：Commit**

```bash
git add OverlayTranslate/Services/
git add OverlayTranslate/App.xaml.cs
git commit -m "feat: 截图与图像处理服务 - ScreenshotService, ImageProcessor, TextRenderer, StyleAnalyzer"
```

---

## 任务 7：覆盖层窗口与 UI 控件

**文件：**
- 创建：`OverlayTranslate/Windows/OverlayWindow.xaml`
- 创建：`OverlayTranslate/Windows/OverlayWindow.xaml.cs`
- 创建：`OverlayTranslate/Controls/SelectionCanvas.cs`
- 创建：`OverlayTranslate/Controls/MaskLayer.cs`
- 创建：`OverlayTranslate/Controls/FloatingToolbar.xaml`
- 创建：`OverlayTranslate/Controls/FloatingToolbar.xaml.cs`

- [ ] **步骤 1：创建 OverlayWindow XAML**

```xml
<!-- Windows/OverlayWindow.xaml -->
<Window x:Class="OverlayTranslate.Windows.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:OverlayTranslate.Controls"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        WindowState="Maximized">
    <Grid x:Name="RootGrid">
        <!-- 背景截图层 -->
        <Image x:Name="BackgroundImage" Stretch="Uniform" />
        <!-- 遮罩层 -->
        <controls:MaskLayer x:Name="Mask" />
        <!-- 选区层 -->
        <Canvas x:Name="SelectionLayer" />
        <!-- 浮动工具栏 -->
        <controls:FloatingToolbar x:Name="Toolbar" Visibility="Collapsed" />
    </Grid>
</Window>
```

- [ ] **步骤 2：实现 OverlayWindow 状态机**

```csharp
// Windows/OverlayWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverlayTranslate.Controls;
using OverlayTranslate.Models;
using OverlayTranslate.Services;
using OverlayTranslate.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace OverlayTranslate.Windows;

public enum OverlayState
{
    Idle,
    Selecting,
    Processing,
    Result,
    Exiting
}

public partial class OverlayWindow : Window
{
    private OverlayState _state = OverlayState.Idle;
    private Point _selectionStart;
    private Rect _selectionRect;
    private byte[]? _screenshotData;
    private OcrResult? _ocrResult;
    private TranslationResult? _translationResult;

    private readonly ScreenshotService _screenshotService;
    private readonly ImageProcessor _imageProcessor;
    private readonly TextRenderer _textRenderer;
    private readonly StyleAnalyzer _styleAnalyzer;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITranslationEngine _translationEngine;

    public event Action? OnExit;

    public OverlayWindow(
        ScreenshotService screenshotService,
        ImageProcessor imageProcessor,
        TextRenderer textRenderer,
        StyleAnalyzer styleAnalyzer,
        IOcrEngine ocrEngine,
        ITranslationEngine translationEngine)
    {
        InitializeComponent();
        _screenshotService = screenshotService;
        _imageProcessor = imageProcessor;
        _textRenderer = textRenderer;
        _styleAnalyzer = styleAnalyzer;
        _ocrEngine = ocrEngine;
        _translationEngine = translationEngine;

        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
    }

    public void ShowForSelection()
    {
        _screenshotData = _screenshotService.CaptureFullScreen();
        var bgImage = new BitmapImage();
        using (var stream = new MemoryStream(_screenshotData))
        {
            bgImage.BeginInit();
            bgImage.CacheOption = BitmapCacheOption.OnLoad;
            bgImage.StreamSource = stream;
            bgImage.EndInit();
        }
        BackgroundImage.Source = bgImage;

        Mask.ClearSelection();
        SelectionLayer.Children.Clear();
        Toolbar.Visibility = Visibility.Collapsed;
        _state = OverlayState.Selecting;
        Show();
        Activate();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitOverlay();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state != OverlayState.Selecting) return;
        _selectionStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_state != OverlayState.Selecting) return;
        ReleaseMouseCapture();

        var end = e.GetPosition(this);
        _selectionRect = new Rect(
            Math.Min(_selectionStart.X, end.X),
            Math.Min(_selectionStart.Y, end.Y),
            Math.Abs(end.X - _selectionStart.X),
            Math.Abs(end.Y - _selectionStart.Y));

        if (_selectionRect.Width > 10 && _selectionRect.Height > 10)
        {
            Mask.SetSelection(_selectionRect);
            _ = ProcessSelectionAsync();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_state != OverlayState.Selecting || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        var rect = new Rect(
            Math.Min(_selectionStart.X, current.X),
            Math.Min(_selectionStart.Y, current.Y),
            Math.Abs(current.X - _selectionStart.X),
            Math.Abs(current.Y - _selectionStart.Y));
        Mask.SetSelection(rect);
    }

    private async Task ProcessSelectionAsync()
    {
        _state = OverlayState.Processing;
        try
        {
            var regionImage = _screenshotService.CaptureRegion(_selectionRect);
            _ocrResult = await _ocrEngine.RecognizeAsync(regionImage);

            if (_ocrResult.TextBlocks.Count > 0)
            {
                _translationResult = await _translationEngine.TranslateAsync(
                    _ocrResult.FullText, "auto", "zh-CN");
                ShowTranslationResult();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理失败: {ex.Message}");
        }
        _state = OverlayState.Result;
    }

    private void ShowTranslationResult()
    {
        if (_ocrResult == null || _translationResult == null || _screenshotData == null) return;

        // 对每个文字块进行覆盖和翻译回绘
        var imageData = _screenshotData;
        foreach (var block in _ocrResult.TextBlocks)
        {
            var bgColor = _imageProcessor.SampleBackgroundColor(imageData, block.BoundingBox);
            imageData = _imageProcessor.FillRegion(imageData, block.BoundingBox, bgColor);
        }

        // 简化：显示处理后的图像
        var bgImage = new BitmapImage();
        using (var stream = new MemoryStream(imageData))
        {
            bgImage.BeginInit();
            bgImage.CacheOption = BitmapCacheOption.OnLoad;
            bgImage.StreamSource = stream;
            bgImage.EndInit();
        }
        BackgroundImage.Source = bgImage;

        // 显示工具栏
        Toolbar.SetData(_ocrResult.FullText, _translationResult.TranslatedText);
        Toolbar.Visibility = Visibility.Visible;
        Canvas.SetLeft(Toolbar, _selectionRect.Left);
        Canvas.SetTop(Toolbar, _selectionRect.Bottom + 8);
    }

    private void ExitOverlay()
    {
        _state = OverlayState.Idle;
        Hide();
        OnExit?.Invoke();
    }
}
```

- [ ] **步骤 3：实现 MaskLayer 控件**

```csharp
// Controls/MaskLayer.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OverlayTranslate.Controls;

public class MaskLayer : Canvas
{
    private readonly Rectangle _maskRect;
    private Path? _selectionPath;

    public MaskLayer()
    {
        _maskRect = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            IsHitTestVisible = false
        };
        Children.Add(_maskRect);

        Loaded += (_, _) => UpdateMask();
        SizeChanged += (_, _) => UpdateMask();
    }

    private void UpdateMask()
    {
        _maskRect.Width = ActualWidth;
        _maskRect.Height = ActualHeight;
    }

    public void SetSelection(Rect rect)
    {
        if (_selectionPath != null)
            Children.Remove(_selectionPath);

        // 使用裁剪路径在遮罩上挖出选区
        var geometry = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var holeGeometry = new RectangleGeometry(rect);
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, holeGeometry);

        _maskRect.Clip = combined;
        _maskRect.Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));

        // 选区边框
        _selectionPath = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 113, 227)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection([4, 2]),
            Data = new RectangleGeometry(rect),
            IsHitTestVisible = false
        };
        Children.Add(_selectionPath);
    }

    public void ClearSelection()
    {
        _maskRect.Clip = null;
        _maskRect.Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        if (_selectionPath != null)
        {
            Children.Remove(_selectionPath);
            _selectionPath = null;
        }
    }
}
```

- [ ] **步骤 4：实现 FloatingToolbar**

```xml
<!-- Controls/FloatingToolbar.xaml -->
<UserControl x:Class="OverlayTranslate.Controls.FloatingToolbar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border Background="#FF2D2D2F" BorderBrush="#FF424245" BorderThickness="1" CornerRadius="10"
            Padding="10,8" MinWidth="420">
        <StackPanel Spacing="8">
            <!-- 语言选择行 -->
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="原语言" VerticalAlignment="Center" FontSize="11" Foreground="#FF86868B"/>
                <ComboBox x:Name="SourceLanguageCombo" Width="120" SelectedIndex="0">
                    <ComboBoxItem Content="自动检测"/>
                    <ComboBoxItem Content="中文"/>
                    <ComboBoxItem Content="English"/>
                    <ComboBoxItem Content="日本語"/>
                    <ComboBoxItem Content="한국어"/>
                </ComboBox>
                <TextBlock Text="→" VerticalAlignment="Center" Foreground="#FF86868B"/>
                <TextBlock Text="目标语言" VerticalAlignment="Center" FontSize="11" Foreground="#FF86868B"/>
                <ComboBox x:Name="TargetLanguageCombo" Width="120" SelectedIndex="0">
                    <ComboBoxItem Content="中文(简体)"/>
                    <ComboBoxItem Content="English"/>
                    <ComboBoxItem Content="日本語"/>
                    <ComboBoxItem Content="한국어"/>
                </ComboBox>
            </StackPanel>

            <!-- 引擎选择行 -->
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="OCR" VerticalAlignment="Center" FontSize="11" Foreground="#FF86868B"/>
                <ComboBox x:Name="OcrEngineCombo" Width="120" SelectedIndex="0">
                    <ComboBoxItem Content="PaddleOCR"/>
                    <ComboBoxItem Content="RemoteOCR"/>
                </ComboBox>
                <TextBlock Text="翻译" VerticalAlignment="Center" FontSize="11" Foreground="#FF86868B"/>
                <ComboBox x:Name="TranslationEngineCombo" Width="120" SelectedIndex="0">
                    <ComboBoxItem Content="DeepL"/>
                    <ComboBoxItem Content="Google"/>
                    <ComboBoxItem Content="Baidu"/>
                    <ComboBoxItem Content="OpenAI"/>
                </ComboBox>
            </StackPanel>

            <!-- 操作按钮行 -->
            <StackPanel Orientation="Horizontal" Spacing="6">
                <Button x:Name="ReselectButton" Content="重选" Style="{StaticResource AccentButton}"/>
                <Button x:Name="ShowOriginalButton" Content="显示原文"/>
                <Button x:Name="CopyOriginalButton" Content="复制原文"/>
                <Button x:Name="CopyTranslatedButton" Content="复制译文"/>
                <Button x:Name="ExitButton" Content="退出"/>
            </StackPanel>
        </StackPanel>
    </Border>
</UserControl>
```

```csharp
// Controls/FloatingToolbar.xaml.cs
using System.Windows;
using System.Windows.Controls;

namespace OverlayTranslate.Controls;

public partial class FloatingToolbar : UserControl
{
    private string _originalText = "";
    private string _translatedText = "";
    private bool _showingOriginal = false;

    public event Action? OnReselect;
    public event Action? OnExit;

    public FloatingToolbar()
    {
        InitializeComponent();
        ReselectButton.Click += (_, _) => OnReselect?.Invoke();
        ExitButton.Click += (_, _) => OnExit?.Invoke();
        CopyOriginalButton.Click += (_, _) => Clipboard.SetText(_originalText);
        CopyTranslatedButton.Click += (_, _) => Clipboard.SetText(_translatedText);
        ShowOriginalButton.Click += (_, _) =>
        {
            _showingOriginal = !_showingOriginal;
            ShowOriginalButton.Content = _showingOriginal ? "显示译文" : "显示原文";
        };
    }

    public void SetData(string originalText, string translatedText)
    {
        _originalText = originalText;
        _translatedText = translatedText;
        _showingOriginal = false;
        ShowOriginalButton.Content = "显示原文";
    }
}
```

- [ ] **步骤 5：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 6：Commit**

```bash
git add OverlayTranslate/Windows/OverlayWindow.xaml OverlayTranslate/Windows/OverlayWindow.xaml.cs
git add OverlayTranslate/Controls/
git commit -m "feat: 覆盖层窗口与 UI 控件 - OverlayWindow, MaskLayer, FloatingToolbar"
```

---

## 任务 8：系统托盘与全局热键

**文件：**
- 创建：`OverlayTranslate/Infrastructure/HotkeyManager.cs`
- 创建：`OverlayTranslate/Infrastructure/TrayIconManager.cs`
- 修改：`OverlayTranslate/MainWindow.xaml`
- 修改：`OverlayTranslate/MainWindow.xaml.cs`
- 修改：`OverlayTranslate/App.xaml.cs`

- [ ] **步骤 1：实现 HotkeyManager**

```csharp
// Infrastructure/HotkeyManager.cs
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OverlayTranslate.Infrastructure;

public class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly int _hotkeyId;
    private HwndSource? _source;
    private Action? _onHotkey;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyManager(int hotkeyId = 9000)
    {
        _hotkeyId = hotkeyId;
    }

    public void Register(Window window, string[] modifiers, string key, Action callback)
    {
        _onHotkey = callback;
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(HwndHook);

        uint modFlags = 0;
        foreach (var mod in modifiers)
        {
            modFlags |= mod.ToLower() switch
            {
                "alt" => 0x0001,
                "ctrl" => 0x0002,
                "shift" => 0x0004,
                "win" => 0x0008,
                _ => 0
            };
        }

        uint vk = key.ToUpper()[0];
        RegisterHotKey(helper.Handle, _hotkeyId, modFlags, vk);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            _onHotkey?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source?.RemoveHook(HwndHook);
        // UnregisterHotKey 需要窗口句柄，在 OnExit 中处理
    }
}
```

- [ ] **步骤 2：实现 TrayIconManager**

```csharp
// Infrastructure/TrayIconManager.cs
using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace OverlayTranslate.Infrastructure;

public class TrayIconManager : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly Action _onScreenshotRequested;

    public TrayIconManager(Action onScreenshotRequested)
    {
        _onScreenshotRequested = onScreenshotRequested;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "OverlayTranslate",
            Icon = SystemIcons.Application, // 后续替换为自定义图标
            ContextMenu = CreateContextMenu()
        };
        _trayIcon.TrayLeftMouseDown += (_, _) => _onScreenshotRequested();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        var screenshotItem = new MenuItem { Header = "截图翻译" };
        screenshotItem.Click += (_, _) => _onScreenshotRequested();
        menu.Items.Add(screenshotItem);

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}
```

- [ ] **步骤 3：更新 MainWindow 为托盘宿主**

```xml
<!-- MainWindow.xaml -->
<Window x:Class="OverlayTranslate.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="OverlayTranslate" Width="0" Height="0"
        WindowStyle="None" ShowInTaskbar="False" Visibility="Hidden">
</Window>
```

```csharp
// MainWindow.xaml.cs
using System.Windows;
using OverlayTranslate.Infrastructure;

namespace OverlayTranslate;

public partial class MainWindow : Window
{
    private readonly TrayIconManager _trayManager;
    private readonly HotkeyManager _hotkeyManager;

    public MainWindow(TrayIconManager trayManager, HotkeyManager hotkeyManager)
    {
        InitializeComponent();
        _trayManager = trayManager;
        _hotkeyManager = hotkeyManager;

        Loaded += (_, _) =>
        {
            _trayManager.Initialize();
            // 热键注册需要窗口句柄，在窗口加载后进行
        };
    }
}
```

- [ ] **步骤 4：在 App.xaml.cs 中注册并启动**

```csharp
// App.xaml.cs 的 ConfigureServices 和 OnStartup 中添加：
// ConfigureServices:
services.AddSingleton<HotkeyManager>();
services.AddSingleton<TrayIconManager>(sp =>
{
    var app = (App)Application.Current;
    return new TrayIconManager(() => app.StartScreenshot());
});
services.AddSingleton<MainWindow>();

// OnStartup:
var mainWindow = Services.GetRequiredService<MainWindow>();
mainWindow.Show();
```

- [ ] **步骤 5：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 6：Commit**

```bash
git add OverlayTranslate/Infrastructure/HotkeyManager.cs OverlayTranslate/Infrastructure/TrayIconManager.cs
git add OverlayTranslate/MainWindow.xaml OverlayTranslate/MainWindow.xaml.cs
git add OverlayTranslate/App.xaml.cs
git commit -m "feat: 系统托盘与全局热键"
```

---

## 任务 9：集成联调

**文件：**
- 修改：`OverlayTranslate/App.xaml.cs`（完整集成）
- 修改：`OverlayTranslate/Windows/OverlayWindow.xaml.cs`（工具栏事件绑定）

- [ ] **步骤 1：完善 App.xaml.cs 集成**

```csharp
// App.xaml.cs - 完整集成版本
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Engines;
using OverlayTranslate.Engines.Ocr;
using OverlayTranslate.Engines.Translation;
using OverlayTranslate.Services;
using OverlayTranslate.Windows;
using Serilog;

namespace OverlayTranslate;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;
    private OverlayWindow? _overlayWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configManager = new ConfigManager();
        configManager.Load();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(configManager.Settings.Logging.File, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();
        ConfigureServices(services, configManager);
        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    public void StartScreenshot()
    {
        _overlayWindow ??= Services.GetRequiredService<OverlayWindow>();
        _overlayWindow.ShowForSelection();
    }

    private void ConfigureServices(IServiceCollection services, ConfigManager configManager)
    {
        services.AddSingleton(configManager);
        services.AddHttpClient();

        // 服务
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<ImageProcessor>();
        services.AddSingleton<StyleAnalyzer>();
        services.AddSingleton<TextRenderer>();

        // OCR 引擎
        services.AddSingleton<PaddleOcrEngine>(sp =>
        {
            var cfg = configManager.Settings.Ocr.Engines.GetValueOrDefault("PaddleOCR");
            return new PaddleOcrEngine(cfg?.GetValueOrDefault("modelPath") ?? "inference/");
        });
        services.AddSingleton<RemoteOcrEngine>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var cfg = configManager.Settings.Ocr.Engines.GetValueOrDefault("RemoteOCR");
            return new RemoteOcrEngine(http, cfg?.GetValueOrDefault("endpoint") ?? "", cfg?.GetValueOrDefault("apiKey") ?? "");
        });

        // 翻译引擎
        services.AddSingleton<DeepLTranslationEngine>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var cfg = configManager.Settings.Translation.Engines.GetValueOrDefault("DeepL");
            return new DeepLTranslationEngine(http, cfg?.GetValueOrDefault("apiKey") ?? "", cfg?.GetValueOrDefault("freeTier") == "true");
        });
        services.AddSingleton<GoogleTranslationEngine>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new GoogleTranslationEngine(http);
        });

        // 基础设施
        services.AddSingleton<HotkeyManager>();
        services.AddSingleton<TrayIconManager>(sp => new TrayIconManager(() => StartScreenshot()));
        services.AddSingleton<MainWindow>();
        services.AddTransient<OverlayWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
```

- [ ] **步骤 2：绑定 OverlayWindow 工具栏事件**

```csharp
// OverlayWindow.xaml.cs 构造函数末尾添加：
Toolbar.OnReselect += () =>
{
    Mask.ClearSelection();
    SelectionLayer.Children.Clear();
    Toolbar.Visibility = Visibility.Collapsed;
    _state = OverlayState.Selecting;
};
Toolbar.OnExit += ExitOverlay;
```

- [ ] **步骤 3：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 4：Commit**

```bash
git add OverlayTranslate/App.xaml.cs OverlayTranslate/Windows/OverlayWindow.xaml.cs
git commit -m "feat: 集成联调 - 托盘触发截图翻译完整流程"
```

---

## 任务 10：Python.NET 互操作（可选扩展）

**文件：**
- 创建：`OverlayTranslate/Python/PythonRuntime.cs`
- 创建：`OverlayTranslate/Python/PythonBridge.cs`

- [ ] **步骤 1：实现 PythonRuntime**

```csharp
// Python/PythonRuntime.cs
using Python.Runtime;
using Serilog;

namespace OverlayTranslate.Python;

public class PythonRuntime : IDisposable
{
    private bool _initialized;

    public void Initialize(string? pythonHome = null)
    {
        if (_initialized) return;

        try
        {
            if (!string.IsNullOrEmpty(pythonHome))
                Runtime.PythonDLL = pythonHome;

            PythonEngine.Initialize();
            _initialized = true;
            Log.Information("Python 运行时初始化成功");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Python 运行时初始化失败");
        }
    }

    public dynamic? Execute(string code)
    {
        if (!_initialized) return null;
        using (Py.GIL())
        {
            return PythonEngine.Eval(code);
        }
    }

    public void Dispose()
    {
        if (_initialized)
        {
            PythonEngine.Shutdown();
            _initialized = false;
        }
    }
}
```

- [ ] **步骤 2：实现 PythonBridge**

```csharp
// Python/PythonBridge.cs
using Python.Runtime;
using Serilog;

namespace OverlayTranslate.Python;

public class PythonBridge
{
    private readonly PythonRuntime _runtime;

    public PythonBridge(PythonRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 调用 Python 模块中的函数
    /// </summary>
    public T? CallFunction<T>(string moduleName, string functionName, params object[] args)
    {
        try
        {
            using (Py.GIL())
            {
                dynamic module = Py.Import(moduleName);
                dynamic result = module.InvokeMethod(functionName, args.Select(a => a.ToPython()).ToArray());
                return result.As<T>();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Python 调用失败: {Module}.{Function}", moduleName, functionName);
            return default;
        }
    }
}
```

- [ ] **步骤 3：在 DI 中注册**

```csharp
// App.xaml.cs 的 ConfigureServices 方法中添加：
services.AddSingleton<PythonRuntime>(sp =>
{
    var config = sp.GetRequiredService<ConfigManager>();
    var runtime = new PythonRuntime();
    runtime.Initialize(config.Settings.Python.RuntimePath);
    return runtime;
});
services.AddSingleton<PythonBridge>();
```

- [ ] **步骤 4：构建验证**

运行：`dotnet build OverlayTranslate/OverlayTranslate.csproj`
预期：构建成功

- [ ] **步骤 5：Commit**

```bash
git add OverlayTranslate/Python/
git add OverlayTranslate/App.xaml.cs
git commit -m "feat: Python.NET 互操作层"
```

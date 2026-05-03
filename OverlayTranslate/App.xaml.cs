using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Engines;
using OverlayTranslate.Engines.Ocr;
using OverlayTranslate.Engines.Translation;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Services;
using OverlayTranslate.Windows;
using Serilog;
using Serilog.Events;

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
        var logLevel = Enum.TryParse<LogEventLevel>(
            configManager.Settings.Logging.Level, true, out var level)
            ? level : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.File(configManager.Settings.Logging.File, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // 配置 DI
        var services = new ServiceCollection();
        ConfigureServices(services, configManager);
        Services = services.BuildServiceProvider();

        // 启动托盘（后续任务实现）
        // Services.GetRequiredService<TrayIconManager>();

        // 显示主窗口（托盘宿主）
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    public void StartScreenshot()
    {
        var overlay = Services.GetRequiredService<OverlayWindow>();
        overlay.ShowForSelection();
    }

    private void ConfigureServices(IServiceCollection services, ConfigManager configManager)
    {
        services.AddSingleton(configManager);
        services.AddHttpClient();

        // 注册 OCR 引擎
        services.AddSingleton<IOcrEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var modelPath = config.Settings.Ocr.Engines
                .GetValueOrDefault("PaddleOCR")
                ?.GetValueOrDefault("modelPath") ?? "inference/";
            return new PaddleOcrEngine(modelPath);
        });

        // 注册远程 OCR 引擎
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

        // 注册翻译引擎
        services.AddSingleton<ITranslationEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var cfg = config.Settings.Translation.Engines.GetValueOrDefault("DeepL");
            return new DeepLTranslationEngine(http, cfg?.GetValueOrDefault("apiKey") ?? "", cfg?.GetValueOrDefault("freeTier") == "true");
        });

        services.AddSingleton<ITranslationEngine>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new GoogleTranslationEngine(http);
        });

        services.AddSingleton<ITranslationEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var cfg = config.Settings.Translation.Engines.GetValueOrDefault("Baidu");
            return new BaiduTranslationEngine(http, cfg?.GetValueOrDefault("appId") ?? "", cfg?.GetValueOrDefault("secret") ?? "");
        });

        services.AddSingleton<ITranslationEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var cfg = config.Settings.Translation.Engines.GetValueOrDefault("OpenAI");
            return new OpenAiTranslationEngine(http, cfg?.GetValueOrDefault("apiKey") ?? "", cfg?.GetValueOrDefault("model") ?? "gpt-4o-mini");
        });

        // 注册截图与图像处理服务
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<ImageProcessor>();
        services.AddSingleton<StyleAnalyzer>();
        services.AddSingleton<TextRenderer>();

        // 注册覆盖层窗口（Transient 每次创建新实例）
        services.AddTransient<OverlayWindow>();

        // 注册系统托盘与热键
        services.AddSingleton<HotkeyManager>();
        services.AddSingleton<TrayIconManager>(sp =>
            new TrayIconManager(() => ((App)Application.Current).StartScreenshot()));
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

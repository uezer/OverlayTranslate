using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Engines;
using OverlayTranslate.Engines.Ocr;
using OverlayTranslate.Infrastructure;
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

        // 后续任务添加更多服务注册
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

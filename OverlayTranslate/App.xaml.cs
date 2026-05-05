using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Engines;
using OverlayTranslate.Engines.Ocr;
using OverlayTranslate.Engines.Translation;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Localization;
using OverlayTranslate.Services;
using OverlayTranslate.ViewModels;
using OverlayTranslate.Windows;
using Serilog;
using Serilog.Events;

namespace OverlayTranslate;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;
    private OverlayWindow? _currentOverlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常处理
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "未处理的 UI 异常");
            MessageBox.Show($"{LocManager.Get("App_StartupException")} {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                LocManager.Get("App_StartupError"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "未处理的域异常");
        };

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

        // 初始化主题
        var theme = configManager.Settings.Other.Theme;
        ThemeManager.SetTheme(theme);

        // 初始化多语言
        LocManager.Initialize(configManager.Settings.Other.Locale);

        // 显示主窗口（托盘宿主）
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // 异步检查更新（不阻塞启动）
        _ = CheckForUpdateAsync();
    }

    public void StartScreenshot()
    {
        _currentOverlay?.Close();
        _currentOverlay = Services.GetRequiredService<OverlayWindow>();
        _currentOverlay.ShowForSelection();
    }

    private void ConfigureServices(IServiceCollection services, ConfigManager configManager)
    {
        services.AddSingleton(configManager);
        services.AddHttpClient();

        // 注册 OCR 引擎（具体类型）
        services.AddSingleton<PaddleOcrEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var modelPath = config.Settings.Ocr.Engines
                .GetValueOrDefault("PaddleOCR")
                ?.GetValueOrDefault("modelPath") ?? "inference/";
            return new PaddleOcrEngine(modelPath);
        });

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

        // 根据配置选择默认 OCR 引擎，带自动回退
        services.AddSingleton<IOcrEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var activeEngine = config.Settings.Ocr.ActiveEngine;
            var fallback = config.Settings.Ocr.FallbackEngine;

            IOcrEngine? primary = activeEngine switch
            {
                "PaddleOCR" => sp.GetRequiredService<PaddleOcrEngine>(),
                "RemoteOCR" => sp.GetRequiredService<RemoteOcrEngine>(),
                _ => sp.GetRequiredService<PaddleOcrEngine>()
            };

            if (primary.IsAvailable) return primary;

            // 主引擎不可用，尝试回退
            if (!string.IsNullOrEmpty(fallback))
            {
                IOcrEngine? fb = fallback switch
                {
                    "PaddleOCR" => sp.GetRequiredService<PaddleOcrEngine>(),
                    "RemoteOCR" => sp.GetRequiredService<RemoteOcrEngine>(),
                    _ => null
                };
                if (fb?.IsAvailable == true) return fb;
            }

            // 回退也不可用，尝试另一个
            if (activeEngine != "RemoteOCR")
            {
                var remote = sp.GetRequiredService<RemoteOcrEngine>();
                if (remote.IsAvailable) return remote;
            }
            else
            {
                var paddle = sp.GetRequiredService<PaddleOcrEngine>();
                if (paddle.IsAvailable) return paddle;
            }

            // 都不可用，返回主引擎（会抛异常，但有日志）
            Log.Warning("所有 OCR 引擎均不可用，主引擎: {Engine}", activeEngine);
            return primary;
        });

        // 注册翻译引擎（具体类型）
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

        services.AddSingleton<MicrosoftTranslationEngine>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new MicrosoftTranslationEngine(http);
        });

        // 根据配置选择默认翻译引擎，带自动回退（跳过未配置 API key 的引擎）
        services.AddSingleton<ITranslationEngine>(sp =>
        {
            var config = sp.GetRequiredService<ConfigManager>();
            var activeEngine = config.Settings.Translation.ActiveEngine;
            var fallback = config.Settings.Translation.FallbackEngine;

            ITranslationEngine Resolve(string name) => name switch
            {
                "DeepL" => sp.GetRequiredService<DeepLTranslationEngine>(),
                "Google" => sp.GetRequiredService<GoogleTranslationEngine>(),
                "Baidu" => sp.GetRequiredService<BaiduTranslationEngine>(),
                "OpenAI" => sp.GetRequiredService<OpenAiTranslationEngine>(),
                "Microsoft" => sp.GetRequiredService<MicrosoftTranslationEngine>(),
                _ => sp.GetRequiredService<GoogleTranslationEngine>()
            };

            // 尝试主引擎
            var primary = Resolve(activeEngine);
            if (primary.IsAvailable)
            {
                Log.Information("使用翻译引擎: {Engine}", primary.Name);
                return primary;
            }

            // 主引擎不可用，尝试配置的回退引擎
            if (!string.IsNullOrEmpty(fallback))
            {
                var fb = Resolve(fallback);
                if (fb.IsAvailable)
                {
                    Log.Information("主翻译引擎 {Active} 不可用，使用回退引擎: {Engine}", activeEngine, fb.Name);
                    return fb;
                }
            }

            // 遍历所有引擎，找第一个可用的
            var allEngines = new ITranslationEngine[]
            {
                sp.GetRequiredService<GoogleTranslationEngine>(),
                sp.GetRequiredService<DeepLTranslationEngine>(),
                sp.GetRequiredService<BaiduTranslationEngine>(),
                sp.GetRequiredService<OpenAiTranslationEngine>(),
                sp.GetRequiredService<MicrosoftTranslationEngine>()
            };
            var available = allEngines.FirstOrDefault(e => e.IsAvailable);
            if (available != null)
            {
                Log.Information("主翻译引擎 {Active} 不可用，自动选择: {Engine}", activeEngine, available.Name);
                return available;
            }

            // 都不可用
            Log.Warning("所有翻译引擎均不可用，主引擎: {Engine}", activeEngine);
            return primary;
        });

        // 注册引擎字典（用于运行时切换）
        services.AddSingleton<Dictionary<string, IOcrEngine>>(sp => new()
        {
            ["PaddleOCR"] = sp.GetRequiredService<PaddleOcrEngine>(),
            ["RemoteOCR"] = sp.GetRequiredService<RemoteOcrEngine>()
        });
        services.AddSingleton<Dictionary<string, ITranslationEngine>>(sp => new()
        {
            ["Google"] = sp.GetRequiredService<GoogleTranslationEngine>(),
            ["DeepL"] = sp.GetRequiredService<DeepLTranslationEngine>(),
            ["百度"] = sp.GetRequiredService<BaiduTranslationEngine>(),
            ["OpenAI"] = sp.GetRequiredService<OpenAiTranslationEngine>(),
            ["Microsoft"] = sp.GetRequiredService<MicrosoftTranslationEngine>()
        });

        // 注册引擎显示名映射
        services.AddSingleton<EngineDisplayMap>();

        // 注册截图与图像处理服务
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<ImageProcessor>();
        services.AddSingleton<StyleAnalyzer>();

        // 注册翻译管线服务
        services.AddSingleton<TranslationPipeline>();

        // 注册覆盖层窗口 ViewModel（Transient 每次创建新实例）
        services.AddTransient<OverlayWindowViewModel>();
        services.AddTransient<FloatingToolbarViewModel>();
        services.AddTransient<SettingsViewModel>();

        // 注册覆盖层窗口（Transient 每次创建新实例）
        services.AddTransient<OverlayWindow>();

        // 注册系统托盘与热键
        services.AddSingleton<HotkeyManager>();
        services.AddTransient<SettingsWindow>();
        services.AddSingleton<MainWindow>();

        // 注册更新服务
        services.AddSingleton<UpdateService>();
        services.AddTransient<UpdateDialogViewModel>();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var configManager = Services.GetRequiredService<ConfigManager>();
            if (!configManager.Settings.Update.AutoCheck)
                return;

            var updateService = Services.GetRequiredService<UpdateService>();
            var result = await updateService.CheckForUpdateAsync(CancellationToken.None);

            if (result.Kind != UpdateCheckResultKind.UpdateAvailable || result.Release == null)
                return;

            // 检查是否跳过此版本
            var skippedVersion = configManager.Settings.Update.SkippedVersion;
            if (!string.IsNullOrEmpty(skippedVersion) &&
                result.Release.TagName.TrimStart('v') == skippedVersion)
                return;

            await ShowUpdateDialog(result.Release);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "自动检查更新失败");
        }
    }

    /// <summary>
    /// 从托盘菜单手动触发更新检查，通过气泡通知显示结果，发现更新则弹出 UpdateDialog。
    /// </summary>
    public async Task CheckForUpdateFromTrayAsync(
        Action<string, string, H.NotifyIcon.Core.NotificationIcon>? notify = null)
    {
        try
        {
            var updateService = Services.GetRequiredService<UpdateService>();
            var result = await updateService.CheckForUpdateAsync(
                CancellationToken.None, bypassRateLimit: true);

            switch (result.Kind)
            {
                case UpdateCheckResultKind.UpdateAvailable when result.Release != null:
                    // 检查是否跳过此版本
                    var configManager = Services.GetRequiredService<ConfigManager>();
                    var skipped = configManager.Settings.Update.SkippedVersion;
                    if (!string.IsNullOrEmpty(skipped) &&
                        result.Release.TagName.TrimStart('v') == skipped)
                    {
                        notify?.Invoke(
                            LocManager.Get("Update_Title"),
                            LocManager.Get("Update_NoUpdate"),
                            H.NotifyIcon.Core.NotificationIcon.Info);
                    }
                    else
                    {
                        await ShowUpdateDialog(result.Release);
                    }
                    break;

                case UpdateCheckResultKind.UpToDate:
                    notify?.Invoke(
                        LocManager.Get("Update_Title"),
                        LocManager.Get("Update_NoUpdate"),
                        H.NotifyIcon.Core.NotificationIcon.Info);
                    break;

                case UpdateCheckResultKind.Failed:
                    notify?.Invoke(
                        LocManager.Get("Update_Title"),
                        result.ErrorMessage ?? LocManager.Get("Update_CheckFailed"),
                        H.NotifyIcon.Core.NotificationIcon.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "手动检查更新失败");
            notify?.Invoke(
                LocManager.Get("Update_Title"),
                LocManager.Get("Update_CheckFailed"),
                H.NotifyIcon.Core.NotificationIcon.Warning);
        }
    }

    private async Task ShowUpdateDialog(GitHubRelease release)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            var updateService = Services.GetRequiredService<UpdateService>();
            var configManager = Services.GetRequiredService<ConfigManager>();
            var viewModel = new UpdateDialogViewModel(updateService, configManager, release);
            var dialog = new UpdateDialog(viewModel)
            {
                Owner = MainWindow
            };
            dialog.ShowDialog();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

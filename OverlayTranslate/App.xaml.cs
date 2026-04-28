using H.NotifyIcon;
using H.NotifyIcon.Core;
using OverlayTranslate.Models;
using OverlayTranslate.Services;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace OverlayTranslate;

public partial class App : System.Windows.Application
{
    private TaskbarIcon? _trayIcon;
    private JsonSettingsStore? _settingsStore;
    private OverlaySessionController? _sessionController;
    private MainWindow? _settingsWindow;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        AppLogger.Info("Application startup.");

        if (OcrWorkerEntryPoint.IsWorkerInvocation(e.Args))
        {
            AppLogger.Info("OCR worker mode detected.");
            int exitCode = await OcrWorkerEntryPoint.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        _settingsStore = new JsonSettingsStore();
        await _settingsStore.InitializeAsync().ConfigureAwait(true);

        IOcrEngine ocrEngine = new PaddleOcrEngine();

        _sessionController = new OverlaySessionController(
            _settingsStore,
            new ScreenCaptureService(),
            ocrEngine,
            new OrchestratingTranslationProvider(_settingsStore),
            new InPlaceRenderer());

        _trayIcon = CreateTrayIcon();
        _trayIcon.ForceCreate();
        AppLogger.Info("Tray icon created.");

        AppSettings settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        AppLogger.Info($"Settings loaded. StartCaptureOnLaunch={settings.StartCaptureOnLaunch}, TranslationStrategy={settings.TranslationStrategy}, OcrStrategy={settings.OcrStrategy}.");
        if (settings.StartCaptureOnLaunch)
        {
            _ = _sessionController.StartCaptureAsync();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppLogger.Info("Application exit.");
        _sessionController?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private async void OnTrayLeftMouseUp(object? sender, System.Windows.RoutedEventArgs e)
    {
        if (_sessionController is not null)
        {
            AppLogger.Info("Tray left click: start capture requested.");
            await _sessionController.StartCaptureAsync().ConfigureAwait(true);
        }
    }

    private async void OnStartFromTrayClick(object? sender, System.Windows.RoutedEventArgs e)
    {
        if (_sessionController is not null)
        {
            AppLogger.Info("Tray menu: start capture requested.");
            await _sessionController.StartCaptureAsync().ConfigureAwait(true);
        }
    }

    private async void OnOpenSettingsClick(object? sender, System.Windows.RoutedEventArgs e)
    {
        if (_settingsStore is null)
        {
            return;
        }

        AppLogger.Info("Settings window requested.");

        if (_settingsWindow is null)
        {
            _settingsWindow = new MainWindow(_settingsStore);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        AppSettings settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        _settingsWindow.LoadSettings(settings);

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    private void OnExitClick(object? sender, System.Windows.RoutedEventArgs e)
    {
        AppLogger.Info("Tray menu: exit requested.");
        Shutdown();
    }

    private TaskbarIcon CreateTrayIcon()
    {
        MenuItem startItem = new()
        {
            Header = "开始截图翻译",
        };
        startItem.Click += OnStartFromTrayClick;

        MenuItem settingsItem = new()
        {
            Header = "设置",
        };
        settingsItem.Click += OnOpenSettingsClick;

        MenuItem exitItem = new()
        {
            Header = "退出",
        };
        exitItem.Click += OnExitClick;

        ContextMenu trayMenu = new();
        trayMenu.Items.Add(startItem);
        trayMenu.Items.Add(settingsItem);
        trayMenu.Items.Add(new Separator());
        trayMenu.Items.Add(exitItem);

        TaskbarIcon taskbarIcon = new()
        {
            ToolTipText = "OverlayTranslate",
            ContextMenu = trayMenu,
            MenuActivation = PopupActivationMode.RightClick,
            IconSource = new GeneratedIconSource
            {
                Text = "译",
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 34,
            },
        };
        taskbarIcon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        return taskbarIcon;
    }
}

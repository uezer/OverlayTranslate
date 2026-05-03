using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Windows;

namespace OverlayTranslate;

public partial class MainWindow : Window
{
    private TrayIconManager? _trayManager;
    private HotkeyManager? _hotkeyManager;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 从 DI 容器获取依赖（XAML 要求无参构造函数，无法使用构造函数注入）
        var app = (App)Application.Current;
        _hotkeyManager = app.Services.GetRequiredService<HotkeyManager>();

        _trayManager = new TrayIconManager(TrayIcon,
            () => app.StartScreenshot(),
            () =>
            {
                if (_settingsWindow == null)
                {
                    _settingsWindow = app.Services.GetRequiredService<SettingsWindow>();
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                }
                _settingsWindow.Show();
                _settingsWindow.Activate();
            });
        _trayManager.Initialize();

        // 注册全局热键 Ctrl+Shift+T 触发截图翻译
        _hotkeyManager.Register(this, ["Ctrl", "Shift"], "T", () =>
        {
            app.StartScreenshot();
        });
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _trayManager?.Dispose();
    }
}

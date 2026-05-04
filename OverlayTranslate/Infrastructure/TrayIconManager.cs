using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using OverlayTranslate.Localization;

namespace OverlayTranslate.Infrastructure;

public class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly Action _onScreenshotRequested;
    private readonly Action _onOpenSettings;
    private MenuItem? _screenshotItem;
    private MenuItem? _settingsItem;
    private MenuItem? _exitItem;

    public TrayIconManager(TaskbarIcon trayIcon, Action onScreenshotRequested, Action onOpenSettings)
    {
        _trayIcon = trayIcon;
        _onScreenshotRequested = onScreenshotRequested;
        _onOpenSettings = onOpenSettings;
    }

    public void Initialize()
    {
        var uri = new Uri("pack://application:,,,/Assets/app.ico");
        _trayIcon.IconSource = BitmapFrame.Create(uri);
        _trayIcon.ContextMenu = CreateContextMenu();
        _trayIcon.TrayLeftMouseUp += (_, _) => _onScreenshotRequested();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        _screenshotItem = new MenuItem { Header = LocManager.Get("Tray_ScreenshotTranslate") };
        _screenshotItem.Click += (_, _) => _onScreenshotRequested();
        menu.Items.Add(_screenshotItem);

        _settingsItem = new MenuItem { Header = LocManager.Get("Tray_Settings") };
        _settingsItem.Click += (_, _) => _onOpenSettings();
        menu.Items.Add(_settingsItem);

        menu.Items.Add(new Separator());

        _exitItem = new MenuItem { Header = LocManager.Get("Tray_Exit") };
        _exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(_exitItem);

        LocManager.Changed += RefreshMenuText;

        return menu;
    }

    private void RefreshMenuText()
    {
        if (_screenshotItem != null) _screenshotItem.Header = LocManager.Get("Tray_ScreenshotTranslate");
        if (_settingsItem != null) _settingsItem.Header = LocManager.Get("Tray_Settings");
        if (_exitItem != null) _exitItem.Header = LocManager.Get("Tray_Exit");
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
    }
}

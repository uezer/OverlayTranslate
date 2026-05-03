using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;

namespace OverlayTranslate.Infrastructure;

public class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly Action _onScreenshotRequested;
    private readonly Action _onOpenSettings;

    public TrayIconManager(TaskbarIcon trayIcon, Action onScreenshotRequested, Action onOpenSettings)
    {
        _trayIcon = trayIcon;
        _onScreenshotRequested = onScreenshotRequested;
        _onOpenSettings = onOpenSettings;
    }

    public void Initialize()
    {
        _trayIcon.Icon = SystemIcons.Application;
        _trayIcon.ContextMenu = CreateContextMenu();
        _trayIcon.TrayLeftMouseUp += (_, _) => _onScreenshotRequested();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        var screenshotItem = new MenuItem { Header = "截图翻译" };
        screenshotItem.Click += (_, _) => _onScreenshotRequested();
        menu.Items.Add(screenshotItem);

        var settingsItem = new MenuItem { Header = "设置" };
        settingsItem.Click += (_, _) => _onOpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
    }
}

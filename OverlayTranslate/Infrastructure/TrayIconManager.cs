using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;

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
            Icon = SystemIcons.Application,
            ContextMenu = CreateContextMenu()
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => _onScreenshotRequested();
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

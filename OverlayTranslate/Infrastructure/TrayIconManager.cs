using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using OverlayTranslate.Localization;

namespace OverlayTranslate.Infrastructure;

public class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly Action _onScreenshotRequested;
    private readonly Action _onOpenSettings;
    private readonly Func<Action<string, string, NotificationIcon>, Task> _onCheckUpdateAsync;
    private MenuItem? _screenshotItem;
    private MenuItem? _settingsItem;
    private MenuItem? _checkUpdateItem;
    private MenuItem? _exitItem;

    public TrayIconManager(TaskbarIcon trayIcon, Action onScreenshotRequested,
        Action onOpenSettings, Func<Action<string, string, NotificationIcon>, Task> onCheckUpdateAsync)
    {
        _trayIcon = trayIcon;
        _onScreenshotRequested = onScreenshotRequested;
        _onOpenSettings = onOpenSettings;
        _onCheckUpdateAsync = onCheckUpdateAsync;
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

        _checkUpdateItem = new MenuItem { Header = LocManager.Get("Tray_CheckUpdate") };
        _checkUpdateItem.Click += async (_, _) => await OnCheckUpdateClickedAsync();
        menu.Items.Add(_checkUpdateItem);

        menu.Items.Add(new Separator());

        _exitItem = new MenuItem { Header = LocManager.Get("Tray_Exit") };
        _exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(_exitItem);

        LocManager.Changed += RefreshMenuText;

        return menu;
    }

    private async Task OnCheckUpdateClickedAsync()
    {
        if (_checkUpdateItem == null) return;

        // 进入 loading 状态
        _checkUpdateItem.Header = LocManager.Get("Tray_CheckingUpdate");
        _checkUpdateItem.IsEnabled = false;

        try
        {
            await _onCheckUpdateAsync((title, message, icon) =>
            {
                _trayIcon.ShowNotification(title, message, icon);
            });
        }
        finally
        {
            // 恢复状态
            if (_checkUpdateItem != null)
            {
                _checkUpdateItem.Header = LocManager.Get("Tray_CheckUpdate");
                _checkUpdateItem.IsEnabled = true;
            }
        }
    }

    private void RefreshMenuText()
    {
        if (_screenshotItem != null) _screenshotItem.Header = LocManager.Get("Tray_ScreenshotTranslate");
        if (_settingsItem != null) _settingsItem.Header = LocManager.Get("Tray_Settings");
        if (_checkUpdateItem != null && _checkUpdateItem.IsEnabled)
            _checkUpdateItem.Header = LocManager.Get("Tray_CheckUpdate");
        if (_exitItem != null) _exitItem.Header = LocManager.Get("Tray_Exit");
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
    }
}

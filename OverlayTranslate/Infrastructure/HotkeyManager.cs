using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Serilog;

namespace OverlayTranslate.Infrastructure;

public class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly int _hotkeyId;
    private HwndSource? _source;
    private Action? _onHotkey;
    private IntPtr _hWnd;

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
        _hWnd = helper.Handle;
        _source = HwndSource.FromHwnd(_hWnd);
        _source?.AddHook(HwndHook);

        uint modFlags = 0;
        foreach (var mod in modifiers)
        {
            modFlags |= mod.ToLower() switch
            {
                "alt" => 0x0001u,
                "ctrl" => 0x0002u,
                "shift" => 0x0004u,
                "win" => 0x0008u,
                _ => 0u
            };
        }

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Hotkey key cannot be empty", nameof(key));
        uint vk = key.ToUpper()[0];
        if (!RegisterHotKey(_hWnd, _hotkeyId, modFlags, vk))
        {
            Log.Warning("注册全局热键失败 (ID={Id}, Mod={Mod}, Key={Key})，可能被其他程序占用",
                _hotkeyId, modFlags, key);
        }
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
        if (_hWnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hWnd, _hotkeyId);
        }
        _source?.RemoveHook(HwndHook);
    }
}

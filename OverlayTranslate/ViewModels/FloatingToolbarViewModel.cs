using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OverlayTranslate.Localization;
using OverlayTranslate.Models;

namespace OverlayTranslate.ViewModels;

public partial class FloatingToolbarViewModel : ObservableObject
{
    private string _originalText = "";
    private string _translatedText = "";

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _expandPanelVisible;
    [ObservableProperty] private string _viewModeText = LocManager.Get("Toolbar_OriginalText");
    [ObservableProperty] private string _expandButtonText = "▼";

    private OverlayViewMode _currentViewMode = OverlayViewMode.OriginalText;
    private bool _originalBgFill = true;
    private bool _translatedBgFill = true;

    public FloatingToolbarViewModel()
    {
        LocManager.Changed += RefreshLocalizedStrings;
    }

    // 事件回调（由 OverlayWindow 注册）
    public event Action? OnReselect;
    public event Action? OnExit;
    public event Action<string, string>? OnLanguageChanged;
    public event Action<string, string>? OnEngineChanged;
    public event Action<OverlayViewMode>? OnViewModeChanged;
    public event Action? OnDragStarted;
    public event Action<bool>? OnOriginalBgFillChanged;
    public event Action<bool>? OnTranslatedBgFillChanged;

    [RelayCommand]
    private void Reselect() => OnReselect?.Invoke();

    [RelayCommand]
    private void Exit() => OnExit?.Invoke();

    [RelayCommand]
    private void ToggleViewMode()
    {
        _currentViewMode = _currentViewMode switch
        {
            OverlayViewMode.OriginalText => OverlayViewMode.TranslatedText,
            OverlayViewMode.TranslatedText => OverlayViewMode.OriginalText,
            _ => OverlayViewMode.OriginalText
        };
        ViewModeText = _currentViewMode switch
        {
            OverlayViewMode.OriginalText => LocManager.Get("Toolbar_OriginalText"),
            OverlayViewMode.TranslatedText => LocManager.Get("Toolbar_TranslatedText"),
            _ => LocManager.Get("Toolbar_OriginalText")
        };
        OnViewModeChanged?.Invoke(_currentViewMode);
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        ExpandPanelVisible = !ExpandPanelVisible;
        ExpandButtonText = ExpandPanelVisible ? "▲" : "▼";
    }

    [RelayCommand]
    private void ShowOriginalImage()
    {
        _currentViewMode = OverlayViewMode.OriginalImage;
        ViewModeText = LocManager.Get("Toolbar_OriginalText"); // 按钮保持显示"原文"，原图是临时查看
        OnViewModeChanged?.Invoke(OverlayViewMode.OriginalImage);
    }

    [RelayCommand]
    private void CopyOriginal() => TrySetClipboard(_originalText);

    [RelayCommand]
    private void CopyTranslated() => TrySetClipboard(_translatedText);

    // 业务方法（由 OverlayWindow / FloatingToolbar 调用）
    public void SetData(string originalText, string translatedText)
    {
        _originalText = originalText;
        _translatedText = translatedText;
    }

    public void SetViewMode(OverlayViewMode mode)
    {
        _currentViewMode = mode;
        ViewModeText = mode switch
        {
            OverlayViewMode.OriginalText => LocManager.Get("Toolbar_OriginalText"),
            OverlayViewMode.TranslatedText => LocManager.Get("Toolbar_TranslatedText"),
            _ => LocManager.Get("Toolbar_OriginalText")
        };
    }

    public void SetOriginalBgFill(bool value)
    {
        _originalBgFill = value;
        OnOriginalBgFillChanged?.Invoke(value);
    }

    public void SetTranslatedBgFill(bool value)
    {
        _translatedBgFill = value;
        OnTranslatedBgFillChanged?.Invoke(value);
    }

    public bool IsOriginalBgFillEnabled => _originalBgFill;
    public bool IsTranslatedBgFillEnabled => _translatedBgFill;

    public void NotifyDragStarted() => OnDragStarted?.Invoke();
    public void NotifyLanguageChanged(string which, string value) => OnLanguageChanged?.Invoke(which, value);
    public void NotifyEngineChanged(string which, string value) => OnEngineChanged?.Invoke(which, value);

    private void RefreshLocalizedStrings()
    {
        ViewModeText = _currentViewMode switch
        {
            OverlayViewMode.OriginalText => LocManager.Get("Toolbar_OriginalText"),
            OverlayViewMode.TranslatedText => LocManager.Get("Toolbar_TranslatedText"),
            _ => LocManager.Get("Toolbar_OriginalText")
        };
    }

    private static void TrySetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (COMException) { }
    }
}

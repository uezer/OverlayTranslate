using System.Globalization;
using System.Windows;

namespace OverlayTranslate.Localization;

public static class LocManager
{
    public static readonly string[] SupportedLocales = ["", "zh-CN", "en-US"];
    public static readonly string[] SupportedLocaleDisplayKeys = ["Lang_Auto", "Lang_zh-CN", "Lang_en-US"];

    private static readonly Dictionary<string, ResourceDictionary> _cache = [];
    private static ResourceDictionary? _currentDict;
    private static readonly List<WeakReference<LocBindingProxy>> _proxies = [];

    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentCulture;

    public static event Action? Changed;

    public static void Initialize(string? configLocale)
    {
        var locale = string.IsNullOrEmpty(configLocale)
            ? DetectSystemLocale()
            : configLocale;
        LoadDictionary(locale);
    }

    public static void SetLocale(string locale)
    {
        if (string.IsNullOrEmpty(locale))
            locale = DetectSystemLocale();
        LoadDictionary(locale);
    }

    public static string Get(string key)
    {
        if (_currentDict != null && _currentDict.Contains(key))
            return _currentDict[key] as string ?? key;
        return key;
    }

    internal static void RegisterProxy(LocBindingProxy proxy)
    {
        _proxies.Add(new WeakReference<LocBindingProxy>(proxy));
    }

    internal static void RaiseChangedForProxies()
    {
        for (int i = _proxies.Count - 1; i >= 0; i--)
        {
            if (_proxies[i].TryGetTarget(out var proxy))
                proxy.OnChanged();
            else
                _proxies.RemoveAt(i);
        }
    }

    private static string DetectSystemLocale()
    {
        var uiCulture = CultureInfo.CurrentUICulture.Name;
        return uiCulture.StartsWith("zh") ? "zh-CN" : "en-US";
    }

    private static void LoadDictionary(string locale)
    {
        if (!_cache.TryGetValue(locale, out var dict))
        {
            var uri = new Uri($"/OverlayTranslate;component/Localization/Strings.{locale}.xaml", UriKind.Relative);
            dict = Application.LoadComponent(uri) as ResourceDictionary;
            if (dict != null)
                _cache[locale] = dict;
        }

        if (dict == null) return;

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_currentDict != null)
            merged.Remove(_currentDict);

        merged.Add(dict);
        _currentDict = dict;
        CurrentCulture = new CultureInfo(locale);

        Changed?.Invoke();
        RaiseChangedForProxies();
    }
}

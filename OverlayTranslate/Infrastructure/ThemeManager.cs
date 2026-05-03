using Microsoft.Win32;

namespace OverlayTranslate.Infrastructure;

public static class ThemeManager
{
    public static event Action<string>? ThemeChanged;

    public static string GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int v && v == 0 ? "dark" : "light";
        }
        catch
        {
            return "light";
        }
    }

    public static void SetTheme(string theme)
    {
        var resolved = theme == "system" ? GetSystemTheme() : theme;
        var dictUri = resolved == "dark"
            ? new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);

        var app = System.Windows.Application.Current;
        var existing = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.EndsWith("/Light.xaml") == true
                              || d.Source?.OriginalString.EndsWith("/Dark.xaml") == true);
        if (existing != null)
            app.Resources.MergedDictionaries.Remove(existing);

        app.Resources.MergedDictionaries.Insert(0,
            new System.Windows.ResourceDictionary { Source = dictUri });

        ThemeChanged?.Invoke(resolved);
    }
}

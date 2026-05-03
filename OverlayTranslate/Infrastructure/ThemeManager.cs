using System.Windows.Media;
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
        var isDark = resolved == "dark";

        var app = System.Windows.Application.Current;

        // Order matters: Theme.xaml → Skin → AppColors (last wins)
        // 1. Swap HandyControl skin first
        var hcSkinUri = isDark
            ? new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml")
            : new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml");

        var existingSkin = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("SkinDefault") == true
                              || d.Source?.OriginalString.Contains("SkinDark") == true);
        if (existingSkin != null)
            app.Resources.MergedDictionaries.Remove(existingSkin);

        app.Resources.MergedDictionaries.Insert(0,
            new System.Windows.ResourceDictionary { Source = hcSkinUri });

        // 2. Swap app color dictionary after skin (so it overrides)
        var dictUri = isDark
            ? new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);

        var existingColor = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.EndsWith("/Light.xaml") == true
                              || d.Source?.OriginalString.EndsWith("/Dark.xaml") == true);
        if (existingColor != null)
            app.Resources.MergedDictionaries.Remove(existingColor);

        app.Resources.MergedDictionaries.Add(
            new System.Windows.ResourceDictionary { Source = dictUri });

        // --- 3. Override SystemColors for any remaining Aero2 defaults ---
        ApplySystemColors(isDark);

        ThemeChanged?.Invoke(resolved);
    }

    /// <summary>
    /// Override WPF SystemColors to match the current theme.
    /// Aero2 ControlTemplates reference these keys directly (e.g., SystemColors.ControlBrushKey,
    /// SystemColors.MenuBrushKey), so overriding them is necessary for complete dark mode support.
    /// </summary>
    private static void ApplySystemColors(bool dark)
    {
        var app = System.Windows.Application.Current;

        if (dark)
        {
            // Dark theme: map SystemColors to dark palette
            var overrides = new (System.Windows.ResourceKey key, Color color)[]
            {
                (System.Windows.SystemColors.WindowBrushKey,           Color.FromRgb(0x1E, 0x1E, 0x1E)),
                (System.Windows.SystemColors.WindowTextBrushKey,       Color.FromRgb(0xE0, 0xE0, 0xE0)),
                (System.Windows.SystemColors.WindowFrameBrushKey,      Color.FromRgb(0x40, 0x40, 0x40)),

                (System.Windows.SystemColors.ControlBrushKey,          Color.FromRgb(0x2D, 0x2D, 0x2D)),
                (System.Windows.SystemColors.ControlTextBrushKey,      Color.FromRgb(0xE0, 0xE0, 0xE0)),

                (System.Windows.SystemColors.MenuBrushKey,             Color.FromRgb(0x2D, 0x2D, 0x2D)),
                (System.Windows.SystemColors.MenuTextBrushKey,         Color.FromRgb(0xE0, 0xE0, 0xE0)),
                (System.Windows.SystemColors.MenuHighlightBrushKey,    Color.FromRgb(0x50, 0x50, 0x50)),
                (System.Windows.SystemColors.MenuBarBrushKey,          Color.FromRgb(0x2D, 0x2D, 0x2D)),

                (System.Windows.SystemColors.HighlightBrushKey,        Color.FromRgb(0x4C, 0xC2, 0xFF)),
                (System.Windows.SystemColors.HighlightTextBrushKey,    Color.FromRgb(0xFF, 0xFF, 0xFF)),

                (System.Windows.SystemColors.GrayTextBrushKey,         Color.FromRgb(0x65, 0x65, 0x65)),

                (System.Windows.SystemColors.ScrollBarBrushKey,        Color.FromRgb(0x2D, 0x2D, 0x2D)),

                (System.Windows.SystemColors.InfoBrushKey,             Color.FromRgb(0x2D, 0x2D, 0x2D)),
                (System.Windows.SystemColors.InfoTextBrushKey,         Color.FromRgb(0xE0, 0xE0, 0xE0)),

                (System.Windows.SystemColors.ControlDarkBrushKey,      Color.FromRgb(0x40, 0x40, 0x40)),
                (System.Windows.SystemColors.ControlLightBrushKey,     Color.FromRgb(0x50, 0x50, 0x50)),

                (System.Windows.SystemColors.ActiveBorderBrushKey,     Color.FromRgb(0x40, 0x40, 0x40)),
                (System.Windows.SystemColors.InactiveBorderBrushKey,   Color.FromRgb(0x2D, 0x2D, 0x2D)),

                (System.Windows.SystemColors.ActiveCaptionBrushKey,    Color.FromRgb(0x2D, 0x2D, 0x2D)),
                (System.Windows.SystemColors.InactiveCaptionBrushKey,  Color.FromRgb(0x2D, 0x2D, 0x2D)),
                (System.Windows.SystemColors.InactiveCaptionTextBrushKey, Color.FromRgb(0x99, 0x99, 0x99)),

                (System.Windows.SystemColors.DesktopBrushKey,          Color.FromRgb(0x00, 0x00, 0x00)),
                (System.Windows.SystemColors.AppWorkspaceBrushKey,     Color.FromRgb(0x25, 0x25, 0x26)),
                (System.Windows.SystemColors.HotTrackBrushKey,         Color.FromRgb(0x00, 0x78, 0xD7)),
            };

            foreach (var (key, color) in overrides)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                app.Resources[key] = brush;
            }
        }
        else
        {
            // Light theme: remove overrides to restore default Windows SystemColors
            var keys = new System.Windows.ResourceKey[]
            {
                System.Windows.SystemColors.WindowBrushKey,
                System.Windows.SystemColors.WindowTextBrushKey,
                System.Windows.SystemColors.WindowFrameBrushKey,
                System.Windows.SystemColors.ControlBrushKey,
                System.Windows.SystemColors.ControlTextBrushKey,
                System.Windows.SystemColors.MenuBrushKey,
                System.Windows.SystemColors.MenuTextBrushKey,
                System.Windows.SystemColors.MenuHighlightBrushKey,
                System.Windows.SystemColors.MenuBarBrushKey,
                System.Windows.SystemColors.HighlightBrushKey,
                System.Windows.SystemColors.HighlightTextBrushKey,
                System.Windows.SystemColors.GrayTextBrushKey,
                System.Windows.SystemColors.ScrollBarBrushKey,
                System.Windows.SystemColors.InfoBrushKey,
                System.Windows.SystemColors.InfoTextBrushKey,
                System.Windows.SystemColors.ControlDarkBrushKey,
                System.Windows.SystemColors.ControlLightBrushKey,
                System.Windows.SystemColors.ActiveBorderBrushKey,
                System.Windows.SystemColors.InactiveBorderBrushKey,
                System.Windows.SystemColors.ActiveCaptionBrushKey,
                System.Windows.SystemColors.InactiveCaptionBrushKey,
                System.Windows.SystemColors.InactiveCaptionTextBrushKey,
                System.Windows.SystemColors.DesktopBrushKey,
                System.Windows.SystemColors.AppWorkspaceBrushKey,
                System.Windows.SystemColors.HotTrackBrushKey,
            };

            foreach (var key in keys)
            {
                app.Resources.Remove(key);
            }
        }
    }
}

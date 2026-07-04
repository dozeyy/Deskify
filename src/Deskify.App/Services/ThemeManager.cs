using System.Windows;
using System.Windows.Interop;
using Deskify.Interop;

namespace Deskify.Services;

/// <summary>Swaps the app's color-brush dictionary. Structural styles (Styles.xaml)
/// reference brush keys via DynamicResource, so replacing the merged dictionary
/// re-resolves every brush live — no restart needed to see a new theme.</summary>
public static class ThemeManager
{
    public const string Default = "Dark";
    public static readonly string[] Available = [Default, "Light"];

    /// <summary>Whether the active theme is dark — drives the native title bar tint.</summary>
    public static bool IsDark { get; private set; } = true;

    public static void Apply(string themeName)
    {
        var file = Available.Contains(themeName) ? themeName : Default;
        var uri = new Uri($"UI/Themes/{file}.xaml", UriKind.Relative);
        IsDark = file == Default;

        var app = Application.Current;
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("UI/Styles.xaml", UriKind.Relative) });

        // Retint the native title bar of every open window so the OS chrome
        // follows the theme instead of staying stuck white in dark mode.
        foreach (Window window in app.Windows)
            ApplyTitleBar(window);
    }

    /// <summary>Matches the native (OS-drawn) title bar to the theme via DWM's
    /// immersive-dark-mode flag. Call once per window — before the window handle
    /// exists it defers itself to SourceInitialized. Safe no-op on Windows builds
    /// without the attribute.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => ApplyTitleBar(window);
            return;
        }
        int dark = IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}

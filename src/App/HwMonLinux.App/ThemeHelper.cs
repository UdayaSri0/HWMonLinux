using Avalonia;
using Avalonia.Styling;
using HwMonLinux.App.Models;

namespace HwMonLinux.App;

public static class ThemeHelper
{
    public static void ApplyTheme(ThemeMode mode)
    {
        if (Application.Current is null)
        {
            return;
        }

        var variant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        Application.Current.RequestedThemeVariant = variant;
    }
}

using Avalonia;
using Avalonia.Styling;

namespace Cothon;

/// <summary>Appearance setting → runtime theme variant.</summary>
public static class UiAppearance
{
    public static ThemeVariant Resolve(string setting) => setting switch
    {
        "dark" => ThemeVariant.Dark,
        "system" => SystemPrefersDark() ? ThemeVariant.Dark : ThemeVariant.Light,
        _ => ThemeVariant.Light,
    };

    /// <summary>Windows personalization: AppsUseLightTheme=0 means dark apps.</summary>
    private static bool SystemPrefersDark()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}

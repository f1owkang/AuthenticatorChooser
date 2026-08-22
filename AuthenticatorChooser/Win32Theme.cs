using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace AuthenticatorChooser;

/// <summary>Win32/DWM helpers that give the tray context menu Windows 11 styling: rounded corners and immersive dark mode.</summary>
internal static class Win32Theme {

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE  = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND                   = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    /// <summary>True when the user has chosen the dark app theme in Windows settings.</summary>
    public static bool isDarkTheme() {
        try {
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return value is int appsUseLightTheme && appsUseLightTheme == 0;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>Applies Windows 11 rounded corners and light/dark mode to a window, such as the tray context menu.</summary>
    public static void applyWin11Style(IntPtr hwnd) {
        if (hwnd == IntPtr.Zero) {
            return;
        }
        int darkMode = isDarkTheme() ? 1 : 0;
        int corner   = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

}

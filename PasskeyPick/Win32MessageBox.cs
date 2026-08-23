using System.Runtime.InteropServices;

namespace PasskeyPick;

/// <summary>Shows the native Windows message box (User32), since the app has no visible window for WinForms-style dialogs.</summary>
internal static class Win32MessageBox {

    internal enum Kind {
        Information = 0x00000040, // MB_ICONINFORMATION
        Warning     = 0x00000030, // MB_ICONWARNING
        Error       = 0x00000010, // MB_ICONERROR
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static void show(string text, string caption, Kind kind) {
        _ = MessageBoxW(nint.Zero, text, caption, 0x00000000 | (uint)kind | 0x00001000 /* MB_TOPMOST */);
    }

}

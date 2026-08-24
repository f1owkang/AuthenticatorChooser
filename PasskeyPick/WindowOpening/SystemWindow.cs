using System.Runtime.InteropServices;
using System.Text;

namespace PasskeyPick.WindowOpening;

/// <summary>
/// Minimal in-repo replacement for mwinapi's <c>SystemWindow</c> (a top-level window handle wrapper), so the program
/// no longer depends on that single-maintainer package. Only the members this program uses are provided: the handle,
/// the window class name, the foreground window, and top-level window enumeration.
/// </summary>
public class SystemWindow {

    public SystemWindow(IntPtr hWnd) => HWnd = hWnd;

    /// <summary>The wrapped native window handle.</summary>
    public IntPtr HWnd { get; }

    /// <summary>The window's class name, or the empty string if the window is gone or has no class.</summary>
    public string ClassName {
        get {
            var buffer = new StringBuilder(256);
            return GetClassName(HWnd, buffer, buffer.Capacity) == 0 ? "" : buffer.ToString();
        }
    }

    /// <summary>The foreground window; <see cref="HWnd"/> is zero when there is none.</summary>
    public static SystemWindow ForegroundWindow => new(GetForegroundWindow());

    /// <summary>All top-level windows matching <paramref name="filter"/> (like mwinapi: EnumWindows covers top-level
    /// windows, including invisible ones).</summary>
    public static IEnumerable<SystemWindow> FilterToplevelWindows(Predicate<SystemWindow> filter) {
        var matches = new List<SystemWindow>();
        EnumWindows((hwnd, _) => {
            var window = new SystemWindow(hwnd);
            if (filter(window)) {
                matches.Add(window);
            }
            return true;
        }, IntPtr.Zero);
        return matches;
    }

    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

}

using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>Routes transient user notifications through the tray icon's balloon (bottom-right), so the whole
/// app notifies consistently instead of popping modal boxes. Static so background threads (bridge, keep-alive) can
/// notify without plumbing events through the tray context.</summary>
internal static class TrayNotifications {

    private static NotifyIcon? trayIcon;

    public static void initialize(NotifyIcon? icon) => trayIcon = icon;

    /// <summary>Shows a balloon with localized title/body, optionally formatted. Callable from background threads:
    /// ShowBalloonTip generally works cross-thread via the icon's hidden message window, but only while the tray icon
    /// is alive and its message pump is running; omit the argument for no format args. When the message loop has not
    /// started pumping yet (the startup balloon from the tray-context constructor), ShowBalloonTip is dropped on some
    /// Windows versions, so the balloon is deferred to the first <see cref="Application.Idle"/>.</summary>
    public static void show(string titleKey, string bodyKey, ToolTipIcon icon, params object?[]? formatArgs) {
        string title = UiLanguage.get(titleKey);
        string body  = string.Format(UiLanguage.get(bodyKey), formatArgs ?? []);
        if (Application.MessageLoop) {
            showBalloon(title, body, icon);
            return;
        }
        void deferred(object? sender, EventArgs e) {
            Application.Idle -= deferred;
            showBalloon(title, body, icon);
        }
        Application.Idle += deferred;
    }

    private static void showBalloon(string title, string body, ToolTipIcon icon) {
        try {
            trayIcon?.ShowBalloonTip(10_000, title, body, icon);
        } catch (ObjectDisposedException) {
            // tray icon already disposed during shutdown; dropping the notification is fine
        }
    }
}

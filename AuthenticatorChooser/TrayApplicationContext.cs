using AuthenticatorChooser.WindowOpening;
using AuthenticatorChooser.Windows11;
using ManagedWinapi.Windows;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AuthenticatorChooser;

/// <summary>WinForms application context owning the tray icon, its context menu, and the FIDO dialog listener.</summary>
public sealed class TrayApplicationContext : ApplicationContext {

    private readonly NotifyIcon trayIcon;
    private WindowListener? fidoListener;

    public TrayApplicationContext(ChooserOptions options) {
        var trayMenu = new SystemTrayMenu(options);
        trayMenu.ExitRequested += exit;

        trayIcon = new NotifyIcon {
            Icon             = loadIcon(),
            Text             = "AuthenticatorChooser",
            Visible          = true,
            ContextMenuStrip = trayMenu.MenuStrip
        };

        startBackgroundFidoListener(options);
    }

    private static Icon? loadIcon() {
        // Loaded from an embedded resource so the icon works even when the EXE is published framework-dependent.
        using Stream? iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuthenticatorChooser.YubiKey.ico");
        return iconStream != null ? new Icon(iconStream) : null;
    }

    /// <summary>Runs the background Windows Security FIDO dialog listener on the WinForms UI thread, which pumps WinEvent messages.</summary>
    private void startBackgroundFidoListener(ChooserOptions options) {
        var securityKeyChooser = new WindowsChooser(options);

        fidoListener = new WindowListenerImpl();
        fidoListener.windowOpened += (_, window) => securityKeyChooser.chooseUsbSecurityKey(window);
        foreach (SystemWindow fidoPromptWindow in SystemWindow.FilterToplevelWindows(securityKeyChooser.isFidoPromptWindow)) {
            securityKeyChooser.chooseUsbSecurityKey(fidoPromptWindow);
        }
    }

    private void exit() {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        fidoListener?.Dispose();
        Startup.requestExit();
        ExitThread();
    }

}

using PasskeyPick.WindowOpening;
using PasskeyPick.Windows11;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>WinForms application context owning the tray icon, its context menu, and the FIDO dialog listener.</summary>
public sealed class TrayApplicationContext : ApplicationContext {

    private readonly NotifyIcon trayIcon;
    private WindowListener? fidoListener;

    public TrayApplicationContext(ChooserOptions options) {
        var trayMenu = new SystemTrayMenu(options);
        trayMenu.ExitRequested += exit;
        trayMenu.CheckForUpdatesRequested += onCheckForUpdatesRequested;

        trayIcon = new NotifyIcon {
            Icon             = loadIcon(),
            Text             = "PasskeyPick",
            Visible          = true,
            ContextMenuStrip = trayMenu.MenuStrip
        };
        trayIcon.MouseDoubleClick += onTrayIconDoubleClick;

        startBackgroundFidoListener(options);
        startUpdateChecker();

        TrayNotifications.initialize(trayIcon);
        GpgAgentManager.startIfEnabled();
        GpgBridge.startIfEnabled();
    }

    /// <summary>Opens the PIN cache/clear dialog when the tray icon is double-clicked.</summary>
    private void onTrayIconDoubleClick(object? sender, MouseEventArgs e) {
        if (e.Button != MouseButtons.Left) {
            return;
        }
        using var dialog = new PinSetupDialog();
        dialog.ShowDialog();
    }

    /// <summary>Periodically checks GitHub for a newer release (at most once a day) without blocking the tray.</summary>
    private void startUpdateChecker() {
        _ = Task.Run(async () => {
            while (!Startup.EXITING.IsCancellationRequested) {
                if (UpdateChecker.isCheckStale()) {
                    await checkAndNotifyAsync();
                }
                try {
                    await Task.Delay(TimeSpan.FromHours(1), Startup.EXITING);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        });
    }

    /// <summary>Manual "Check for updates…" from the tray menu: always checks and always reports the outcome.</summary>
    private void onCheckForUpdatesRequested() {
        _ = Task.Run(() => checkAndNotifyAsync(force: true));
    }

    private async Task checkAndNotifyAsync(bool force = false) {
        if (!force) {
            UpdateChecker.markChecked();
        }
        string? newerTag = await UpdateChecker.getNewerReleaseTagAsync();
        if (Startup.EXITING.IsCancellationRequested) {
            return;
        }
        if (newerTag is not null) {
            trayIcon.ShowBalloonTip(10_000, UiLanguage.get("updateAvailableTitle"),
                string.Format(UiLanguage.get("updateAvailableBody"), newerTag, UpdateChecker.LATEST_RELEASE_URL), ToolTipIcon.Info);
        } else if (force) {
            trayIcon.ShowBalloonTip(5_000, UiLanguage.get("updateUpToDateTitle"), UiLanguage.get("updateUpToDateBody"), ToolTipIcon.Info);
        }
    }

    private static Icon? loadIcon() {
        // Loaded from an embedded resource so the icon works even when the EXE is published framework-dependent.
        using Stream? iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PasskeyPick.YubiKey.ico");
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
        GpgBridge.stop();
        GpgAgentManager.stop();
        Startup.requestExit();
        ExitThread();
    }

}

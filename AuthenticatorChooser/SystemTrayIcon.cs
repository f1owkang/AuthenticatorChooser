using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AuthenticatorChooser;

/// <summary>
/// A system tray icon that lets the user temporarily enable or disable the automatic security-key selection,
/// and exit the program (issue #57). Previously, the only way to pause the program was to kill it in Task Manager.
/// </summary>
public sealed class SystemTrayIcon: IDisposable {

    private ChooserOptions options;
    private readonly NotifyIcon     notifyIcon;

    private readonly ToolStripMenuItem enableMenuItem;
    private readonly ToolStripMenuItem exitMenuItem;

    public SystemTrayIcon(ChooserOptions options) {
        this.options = options;

        notifyIcon = new NotifyIcon {
            Icon          = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield,
            Text          = "AuthenticatorChooser",
            Visible       = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        enableMenuItem = new ToolStripMenuItem("Enable automatic security key selection") {
            CheckOnClick = true,
            Checked      = options.isEnabled
        };
        enableMenuItem.CheckedChanged += onEnableToggled;

        exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) => Application.Exit();

        notifyIcon.ContextMenuStrip!.Items.Add(enableMenuItem);
        notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        notifyIcon.ContextMenuStrip.Items.Add(exitMenuItem);

        // Double-clicking the icon is a convenient shortcut to toggle enable/disable
        notifyIcon.DoubleClick += (_, _) => enableMenuItem.Checked = !enableMenuItem.Checked;
    }

    private void onEnableToggled(object? sender, EventArgs args) {
        options.isEnabled = enableMenuItem.Checked;
    }

    public void Dispose() {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }

}

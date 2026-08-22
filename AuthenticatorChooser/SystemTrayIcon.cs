using System.Drawing;
using System.Windows.Forms;

namespace AuthenticatorChooser;

/// <summary>
/// A system tray icon that lets the user enable/disable automatic security-key selection, choose a preferred
/// authenticator method (issue #5), switch the UI language (issue #4), toggle start-at-logon, and exit the
/// program (issue #57). The context menu is rendered in a Windows 11 Fluent style.
/// </summary>
public sealed class SystemTrayIcon: IDisposable {

    private ChooserOptions options;
    private readonly NotifyIcon notifyIcon;

    private ToolStripMenuItem enableMenuItem = null!;

    public SystemTrayIcon(ChooserOptions options) {
        this.options = options;

        notifyIcon = new NotifyIcon {
            Icon             = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield,
            Text             = "AuthenticatorChooser",
            Visible          = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        ContextMenuStrip menu = notifyIcon.ContextMenuStrip!;
        ToolStripManager.Renderer = new Win11MenuRenderer();
        menu.Font      = win11Font();
        menu.Padding   = new Padding(4);
        menu.ForeColor = Win11ColorTable.Text;
        menu.Opened    += (_, _) => Win32Theme.applyWin11Style(menu.Handle);

        rebuildMenu();

        // Double-clicking the icon is a convenient shortcut to toggle enable/disable
        notifyIcon.DoubleClick += (_, _) => enableMenuItem.Checked = !enableMenuItem.Checked;
    }

    /// <summary>Rebuilds the context menu so its labels reflect the currently selected UI language and preference state.</summary>
    private void rebuildMenu() {
        // Recreating the checked item below would otherwise fire CheckedChanged with the wrong value, so preserve the current state first
        bool wasEnabled = enableMenuItem?.Checked ?? options.isEnabled;

        ContextMenuStrip menu = notifyIcon.ContextMenuStrip!;
        menu.SuspendLayout();
        menu.Items.Clear();

        menu.Items.Add(enableMenuItem = new ToolStripMenuItem(UiLanguage.get("trayEnableAutomaticSelection")) {
            ForeColor  = Win11ColorTable.Text,
            CheckOnClick = true,
            Checked      = wasEnabled
        });
        enableMenuItem.CheckedChanged += onEnableToggled;

        menu.Items.Add(buildAuthenticatorMenu());
        menu.Items.Add(buildLanguageMenu());

        ToolStripMenuItem autostartMenuItem = new(UiLanguage.get("trayAutostartOnLogon")) {
            ForeColor = Win11ColorTable.Text,
            Checked   = AutostartManager.isEnabled()
        };
        autostartMenuItem.Click += (_, _) => toggleAutostart(autostartMenuItem);
        menu.Items.Add(autostartMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitMenuItem = new(UiLanguage.get("trayExit")) { ForeColor = Win11ColorTable.Text };
        exitMenuItem.Click += (_, _) => Application.Exit();
        menu.Items.Add(exitMenuItem);

        attachChildMenus(menu);
        menu.ResumeLayout();
    }

    private void onEnableToggled(object? sender, EventArgs args) {
        options.isEnabled = enableMenuItem.Checked;
    }

    private void toggleAutostart(ToolStripMenuItem item) {
        if (item.Checked) {
            AutostartManager.disable();
            item.Checked = false;
        } else if (AutostartManager.enable(options)) {
            item.Checked = true;
        } else {
            MessageBox.Show(UiLanguage.get("autostartRegisterFailed"), "AuthenticatorChooser", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Submenu listing the authenticator methods available on this system, auto-enumerated from the registry (issue #5).</summary>
    private ToolStripMenuItem buildAuthenticatorMenu() {
        ToolStripMenuItem menu = new(UiLanguage.get("trayAuthenticator")) { ForeColor = Win11ColorTable.Text };

        menu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorDefault"), null));
        menu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorUsb"), PriorityChooser.USB_KEY));
        menu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorPairNewPhone"), PriorityChooser.PAIR_NEW_PHONE_KEY));
        menu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorUseExistingPhone"), PriorityChooser.USE_EXISTING_PHONE_KEY));

        IReadOnlyList<string> providers = PasskeyProviderEnumerator.enumerate();
        if (providers.Count > 0) {
            menu.DropDownItems.Add(new ToolStripSeparator());
            foreach (string provider in providers) {
                menu.DropDownItems.Add(buildAuthenticatorOption(provider, provider));
            }
        }

        return menu;
    }

    private ToolStripMenuItem buildAuthenticatorOption(string label, string? preference) {
        ToolStripMenuItem item = new(label) {
            ForeColor = Win11ColorTable.Text,
            Checked   = preference is null ? options.preferredAuthenticator is null
                                           : string.Equals(options.preferredAuthenticator, preference, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (_, _) => {
            options.preferredAuthenticator = preference;
            rebuildMenu();
        };
        return item;
    }

    /// <summary>Submenu to switch the UI language at runtime, taking effect immediately (issue #4).</summary>
    private ToolStripMenuItem buildLanguageMenu() {
        ToolStripMenuItem menu = new(UiLanguage.get("trayLanguage")) { ForeColor = Win11ColorTable.Text };

        menu.DropDownItems.Add(buildLanguageOption(UiLanguage.get("trayFollowSystemLanguage"), null));
        foreach ((string name, string displayName) in UiLanguage.SUPPORTED) {
            menu.DropDownItems.Add(buildLanguageOption(displayName, name));
        }

        return menu;
    }

    private ToolStripMenuItem buildLanguageOption(string label, string? cultureName) {
        ToolStripMenuItem item = new(label) {
            ForeColor = Win11ColorTable.Text,
            Checked   = string.Equals(UiLanguage.currentCultureName(), cultureName, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (_, _) => {
            UiLanguage.apply(cultureName);
            rebuildMenu();
        };
        return item;
    }

    /// <summary>Recursively applies the Windows 11 rounded-corner + dark-mode style to every submenu when it opens.</summary>
    private static void attachChildMenus(ToolStripDropDown menu) {
        foreach (ToolStripItem item in menu.Items) {
            if (item is ToolStripDropDownItem dropdownItem) {
                ToolStripDropDown child = dropdownItem.DropDown;
                child.Opened += (_, _) => Win32Theme.applyWin11Style(child.Handle);
                attachChildMenus(child);
            }
        }
    }

    /// <summary>Prefers the Windows 11 font, falling back to Segoe UI when unavailable.</summary>
    private static Font win11Font() {
        foreach (string family in new[] { "Segoe UI Variable Text", "Segoe UI" }) {
            try {
                return new Font(family, 9f);
            } catch (ArgumentException) {
                // font family not installed; try the next one
            }
        }
        return SystemFonts.MenuFont ?? new Font(FontFamily.GenericSansSerif, 9f);
    }

    public void Dispose() {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }

}

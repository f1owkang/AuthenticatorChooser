using System.Windows.Forms;

namespace AuthenticatorChooser;

/// <summary>
/// Builds the system tray context menu as a WinForms <see cref="ContextMenuStrip"/> (the same approach as g-helper),
/// replacing the WinUI 3 MenuFlyout so the app can be published framework-dependent and stay tiny.
/// </summary>
public sealed class SystemTrayMenu {

    private readonly ChooserOptions options;

    private readonly List<ToolStripMenuItem> authenticatorOptions = [];
    private readonly List<ToolStripMenuItem> languageOptions = [];
    private readonly List<ToolStripMenuItem> ttlOptions = [];

    /// <summary>TTL presets offered in the "Expiration (TTL)" submenu; 0 means "until the program exits".</summary>
    private static readonly (int seconds, string resxKey)[] TTL_PRESETS = [
        (300,  "trayTtl5Min"),
        (600,  "trayTtl10Min"),
        (1800, "trayTtl30Min"),
        (3600, "trayTtl1Hour"),
        (0,    "trayTtlUntilExit")
    ];

    // Checkable items whose check state is refreshed every time the menu opens (PIN cache TTL, autostart, ...).
    private readonly List<(ToolStripMenuItem item, Func<bool> isChecked)> checkedItems = [];

    // All items whose text is localized, so the language switch can re-label them in place.
    private readonly List<(Action<string> setText, string resxKey)> localizedItems = [];

    public ContextMenuStrip MenuStrip { get; }
    public event Action? ExitRequested;

    public SystemTrayMenu(ChooserOptions options) {
        this.options = options;
        MenuStrip = new ContextMenuStrip();
        MenuStrip.Opening += (_, _) => refreshStates();
        rebuild();
    }

    private void rebuild() {
        authenticatorOptions.Clear();
        languageOptions.Clear();
        ttlOptions.Clear();
        localizedItems.Clear();
        MenuStrip.Items.Clear();

        // Automatic selection
        MenuStrip.Items.Add(buildToggle(UiLanguage.get("trayEnableAutomaticSelection"), "trayEnableAutomaticSelection",
            () => options.isEnabled,
            () => {
                options.isEnabled = !options.isEnabled;
                Settings.autoSelectEnabled = options.isEnabled;
                Settings.save();
            }));
        MenuStrip.Items.Add(buildAuthenticatorSubmenu());

        MenuStrip.Items.Add(new ToolStripSeparator());

        // Appearance
        MenuStrip.Items.Add(buildLanguageSubmenu());

        MenuStrip.Items.Add(new ToolStripSeparator());

        // System integration
        MenuStrip.Items.Add(buildToggle(UiLanguage.get("trayAutostartOnLogon"), "trayAutostartOnLogon",
            AutostartManager.isEnabled,
            () => {
                if (AutostartManager.isEnabled()) {
                    AutostartManager.disable();
                } else if (!AutostartManager.enable(options)) {
                    Win32MessageBox.show(UiLanguage.get("autostartRegisterFailed"), "AuthenticatorChooser", Win32MessageBox.Kind.Error);
                }
            }));

        // High-frequency action at the top level: cache or clear the PIN (memory-only, encrypted).
        MenuStrip.Items.Add(buildToggle(UiLanguage.get("trayPinCache"), "trayPinCache",
            PinCache.hasCached,
            () => {
                using var dialog = new PinSetupDialog();
                dialog.ShowDialog();
            }));

        // Less-frequent PIN settings (TTL presets and lock/sleep/hibernate clear flags) grouped in a submenu.
        MenuStrip.Items.Add(buildPinSubmenu());

        MenuStrip.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(UiLanguage.get("trayExit"));
        register(text => exitItem.Text = text, "trayExit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        MenuStrip.Items.Add(exitItem);

        updateChoiceChecks();
        updateTtlChecks();
    }

    /// <summary>Builds the "Security key PIN settings" submenu: TTL presets and lock/sleep/hibernate clear flags.</summary>
    private ToolStripMenuItem buildPinSubmenu() {
        var submenu = new ToolStripMenuItem(UiLanguage.get("traySecurityKeyPin"));
        register(text => submenu.Text = text, "traySecurityKeyPin");

        // TTL presets are flattened here so the menu never goes deeper than two levels.
        var ttlLabel = new ToolStripMenuItem(UiLanguage.get("trayTtlSubmenu")) { Enabled = false };
        register(text => ttlLabel.Text = text, "trayTtlSubmenu");
        submenu.DropDownItems.Add(ttlLabel);
        foreach ((int seconds, string resxKey) in TTL_PRESETS) {
            submenu.DropDownItems.Add(buildTtlOption(seconds, resxKey));
        }

        submenu.DropDownItems.Add(new ToolStripSeparator());

        submenu.DropDownItems.Add(buildToggle(UiLanguage.get("trayPinClearOnLock"), "trayPinClearOnLock",
            () => PinCache.clearOnLockEnabled,
            () => PinCache.clearOnLockEnabled = !PinCache.clearOnLockEnabled));
        submenu.DropDownItems.Add(buildToggle(UiLanguage.get("trayPinClearOnSleep"), "trayPinClearOnSleep",
            () => PinCache.clearOnSleepEnabled,
            () => PinCache.clearOnSleepEnabled = !PinCache.clearOnSleepEnabled));
        submenu.DropDownItems.Add(buildToggle(UiLanguage.get("trayPinClearOnHibernate"), "trayPinClearOnHibernate",
            () => PinCache.clearOnHibernateEnabled,
            () => PinCache.clearOnHibernateEnabled = !PinCache.clearOnHibernateEnabled));

        return submenu;
    }

    /// <summary>Builds one TTL preset menu item; the presets are mutually exclusive (single checked item).</summary>
    private ToolStripMenuItem buildTtlOption(int seconds, string resxKey) {
        var item = new ToolStripMenuItem(UiLanguage.get(resxKey)) { Tag = seconds };
        register(text => item.Text = text, resxKey);
        item.Click += (_, _) => {
            PinCache.ttlSecondsValue = seconds;
            updateTtlChecks();
        };
        ttlOptions.Add(item);
        return item;
    }

    /// <summary>Moves the single check mark between the TTL presets according to the current cache TTL.</summary>
    private void updateTtlChecks() {
        foreach (ToolStripMenuItem item in ttlOptions) {
            item.Checked = (int) item.Tag! == PinCache.ttlSecondsValue;
        }
    }

    private ToolStripMenuItem buildToggle(string label, string resxKey, Func<bool> isChecked, Action toggle) {
        var item = new ToolStripMenuItem(label) { Checked = isChecked() };
        register(text => item.Text = text, resxKey);
        item.Click += (_, _) => {
            toggle();
            item.Checked = isChecked();
        };
        checkedItems.Add((item, isChecked));
        return item;
    }

    /// <summary>Re-syncs every check mark when the menu opens, so states like the PIN cache TTL or the scheduled task don't go stale.</summary>
    private void refreshStates() {
        foreach ((ToolStripMenuItem item, Func<bool> isChecked) in checkedItems) {
            item.Checked = isChecked();
        }
        updateChoiceChecks();
        updateTtlChecks();
    }

    private ToolStripMenuItem buildAuthenticatorSubmenu() {
        var submenu = new ToolStripMenuItem(UiLanguage.get("trayAuthenticator"));
        register(text => submenu.Text = text, "trayAuthenticator");
        submenu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorDefault"), null, "trayAuthenticatorDefault"));
        submenu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorUsb"), PriorityChooser.USB_KEY, "trayAuthenticatorUsb"));
        submenu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorPairNewPhone"), PriorityChooser.PAIR_NEW_PHONE_KEY, "trayAuthenticatorPairNewPhone"));
        submenu.DropDownItems.Add(buildAuthenticatorOption(UiLanguage.get("trayAuthenticatorUseExistingPhone"), PriorityChooser.USE_EXISTING_PHONE_KEY, "trayAuthenticatorUseExistingPhone"));

        IReadOnlyList<string> providers = PasskeyProviders.enumerate();
        if (providers.Count > 0) {
            submenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (string provider in providers) {
                submenu.DropDownItems.Add(buildAuthenticatorOption(provider, provider, null));
            }
        }
        return submenu;
    }

    private ToolStripMenuItem buildAuthenticatorOption(string label, string? preference, string? resxKey) {
        var item = new ToolStripMenuItem(label) { Tag = preference };
        if (resxKey != null) {
            register(text => item.Text = text, resxKey);
        }
        item.Click += (_, _) => {
            options.preferredAuthenticator = preference;
            Settings.preferredAuthenticator = preference;
            Settings.save();
            updateChoiceChecks();
        };
        authenticatorOptions.Add(item);
        return item;
    }

    private ToolStripMenuItem buildLanguageSubmenu() {
        var submenu = new ToolStripMenuItem(UiLanguage.get("trayLanguage"));
        register(text => submenu.Text = text, "trayLanguage");
        submenu.DropDownItems.Add(buildLanguageOption(UiLanguage.get("trayFollowSystemLanguage"), null, "trayFollowSystemLanguage"));
        foreach ((string name, string displayName) in UiLanguage.SUPPORTED) {
            submenu.DropDownItems.Add(buildLanguageOption(displayName, name, null));
        }
        return submenu;
    }

    private ToolStripMenuItem buildLanguageOption(string label, string? cultureName, string? resxKey) {
        var item = new ToolStripMenuItem(label) { Tag = cultureName };
        if (resxKey != null) {
            register(text => item.Text = text, resxKey);
        }
        item.Click += (_, _) => {
            UiLanguage.apply(cultureName);
            Settings.uiLanguage = cultureName;
            Settings.save();
            relabel();
        };
        languageOptions.Add(item);
        return item;
    }

    private void register(Action<string> setText, string resxKey) => localizedItems.Add((setText, resxKey));

    /// <summary>Re-labels every localized menu item in place when the UI language changes.</summary>
    private void relabel() {
        foreach ((Action<string> setText, string resxKey) in localizedItems) {
            setText(UiLanguage.get(resxKey));
        }
        updateChoiceChecks();
    }

    /// <summary>Moves the check mark between the chosen authenticator/language option.</summary>
    private void updateChoiceChecks() {
        foreach (ToolStripMenuItem item in authenticatorOptions) {
            string? preference = item.Tag as string;
            item.Checked = preference is null ? options.preferredAuthenticator is null
                                              : string.Equals(options.preferredAuthenticator, preference, StringComparison.OrdinalIgnoreCase);
        }
        foreach (ToolStripMenuItem item in languageOptions) {
            string? cultureName = item.Tag as string;
            item.Checked = string.Equals(UiLanguage.currentCultureName(), cultureName, StringComparison.OrdinalIgnoreCase);
        }
    }

}

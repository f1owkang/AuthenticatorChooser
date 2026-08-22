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

    // All items whose text is localized, so the language switch can re-label them in place.
    private readonly List<(Action<string> setText, string resxKey)> localizedItems = [];

    public ContextMenuStrip MenuStrip { get; }
    public event Action? ExitRequested;

    public SystemTrayMenu(ChooserOptions options) {
        this.options = options;
        MenuStrip = new ContextMenuStrip();
        rebuild();
    }

    private void rebuild() {
        authenticatorOptions.Clear();
        languageOptions.Clear();
        localizedItems.Clear();
        MenuStrip.Items.Clear();

        MenuStrip.Items.Add(buildToggle(UiLanguage.get("trayEnableAutomaticSelection"), "trayEnableAutomaticSelection",
            () => options.isEnabled,
            () => options.isEnabled = !options.isEnabled));

        MenuStrip.Items.Add(buildAuthenticatorSubmenu());
        MenuStrip.Items.Add(buildLanguageSubmenu());

        MenuStrip.Items.Add(buildToggle(UiLanguage.get("trayAutostartOnLogon"), "trayAutostartOnLogon",
            AutostartManager.isEnabled,
            () => {
                if (AutostartManager.isEnabled()) {
                    AutostartManager.disable();
                } else if (!AutostartManager.enable(options)) {
                    Win32MessageBox.show(UiLanguage.get("autostartRegisterFailed"), "AuthenticatorChooser", Win32MessageBox.Kind.Error);
                }
            }));

        MenuStrip.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(UiLanguage.get("trayExit"));
        register(text => exitItem.Text = text, "trayExit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        MenuStrip.Items.Add(exitItem);

        updateChoiceChecks();
    }

    private ToolStripMenuItem buildToggle(string label, string resxKey, Func<bool> isChecked, Action toggle) {
        var item = new ToolStripMenuItem(label) { Checked = isChecked() };
        register(text => item.Text = text, resxKey);
        item.Click += (_, _) => {
            toggle();
            item.Checked = isChecked();
        };
        return item;
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

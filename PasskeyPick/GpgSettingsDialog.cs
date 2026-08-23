using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>The single configuration hub for the GPG features (issues #7, #8): the two enable toggles, the forwarding
/// port, an SSH RemoteForward example, the Gpg4win detection status, and a live status section (closed loop) that
/// shows the actual runtime state and surfaces failures instead of failing silently. Grouped layout: titled
/// "Features", "Forwarding parameters", and "Running status" sections, with the parameter grid using right-aligned
/// two-column alignment and auto-sized control widths.</summary>
internal sealed class GpgSettingsDialog: BaseDialog {

    private readonly CheckBox       bridgeCheck         = new() { AutoSize = true };
    private readonly CheckBox       agentAutostartCheck = new() { AutoSize = true };
    private readonly NumericUpDown  portBox             = new() { Minimum = 1, Maximum = 65535 };
    private readonly TextBox        sshExampleBox       = new() { ReadOnly = true };
    private readonly Label          gpg4winStatus       = new() { AutoSize = true };
    private readonly Label          bridgeStatus        = new() { AutoSize = true };
    private readonly Label          agentStatus         = new() { AutoSize = true };
    private readonly Label          bridgeWarning       = new() { AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleLeft };

    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 2_000 };
    private bool probing;

    public GpgSettingsDialog() {
        // Window title follows the PIN cache dialog convention (PasskeyPick - <title>) for a uniform look.
        Text = $"{Startup.PROGRAM_NAME} - {UiLanguage.get("gpgSettingsTitle")}";

        bridgeCheck.Text         = UiLanguage.get("gpgSettingsBridge");
        bridgeCheck.Checked      = Settings.gpgBridgeEnabled;
        agentAutostartCheck.Text = UiLanguage.get("gpgSettingsAgentAutostart");
        agentAutostartCheck.Checked = Settings.gpgAgentAutostartEnabled;
        bridgeWarning.Text       = UiLanguage.get("gpgSettingsBridgeWarning");
        portBox.Value            = Math.Clamp(Settings.gpgBridgePort, 1, 65535);

        // Both fields auto-size their width to content (no copy button, no fixed width).
        sshExampleBox.Text = buildSshExample((int) portBox.Value);
        fitSshExampleWidth();
        portBox.ValueChanged += (_, _) => {
            sshExampleBox.Text = buildSshExample((int) portBox.Value);
            fitSshExampleWidth();
        };
        portBox.Width = portBox.GetPreferredSize(new Size(int.MaxValue, int.MaxValue)).Width;

        var save  = new Button { Text = UiLanguage.get("gpgSettingsSave"),   AutoSize = true };
        var close = new Button { Text = UiLanguage.get("gpgSettingsCancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => apply();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildSwitchesGroup(), 0, row++);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildParamsGroup(), 0, row++);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildStatusGroup(), 0, row++);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildButtonRow(close, save), 0, row);

        Controls.Add(root);
        AcceptButton = save;   // Enter applies and re-checks; the dialog stays open so the closed loop is visible
        CancelButton = close;  // Esc closes
        statusTimer.Tick += (_, _) => refreshStatus();
        statusTimer.Start();
        FormClosed += (_, _) => statusTimer.Dispose();
        refreshStatus();
    }

    /// <summary>The "Features" section: the two enable toggles side by side, with the bridge security warning (which
    /// also states the ssh -A equivalence) shown directly beneath the toggle it applies to.</summary>
    private GroupBox buildSwitchesGroup() {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(bridgeCheck);
        row.Controls.Add(agentAutostartCheck);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(row, 0, 0);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(bridgeWarning, 0, 1);
        return buildGroup(UiLanguage.get("gpgSettingsSwitches"), layout);
    }

    /// <summary>The "Forwarding parameters" section: right-aligned labels + controls in a two-column grid; the SSH
    /// example box auto-sizes its width to the config text.</summary>
    private GroupBox buildParamsGroup() {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("gpgSettingsPort")), 0, row);
        portBox.Anchor = AnchorStyles.Left;
        layout.Controls.Add(portBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("gpgSettingsSshExample")), 0, row);
        sshExampleBox.Anchor = AnchorStyles.Left;
        layout.Controls.Add(sshExampleBox, 1, row);

        return buildGroup(UiLanguage.get("gpgSettingsParams"), layout);
    }

    /// <summary>The "Running status" section (closed loop): two-column grid (right-aligned label | live value) for
    /// Gpg4win / bridge / agent.</summary>
    private GroupBox buildStatusGroup() {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("gpgSettingsStatusGpg4win")), 0, row);
        gpg4winStatus.Anchor = AnchorStyles.Left;
        layout.Controls.Add(gpg4winStatus, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("gpgSettingsBridge")), 0, row);
        bridgeStatus.Anchor = AnchorStyles.Left;
        layout.Controls.Add(bridgeStatus, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("gpgSettingsStatusAgent")), 0, row);
        agentStatus.Anchor = AnchorStyles.Left;
        layout.Controls.Add(agentStatus, 1, row);

        return buildGroup(UiLanguage.get("gpgSettingsStatus"), layout);
    }

    private static string buildSshExample(int port) => $"RemoteForward <remote socket> 127.0.0.1:{port}";

    /// <summary>Auto-sizes the SSH example box to fit its text.</summary>
    private void fitSshExampleWidth() =>
        sshExampleBox.Width = sshExampleBox.GetPreferredSize(new Size(int.MaxValue, int.MaxValue)).Width + 4;

    private void refreshStatus() {
        gpg4winStatus.Text = GpgTools.resolveGpg4winConnectAgent() is { } path
            ? string.Format(UiLanguage.get("gpgSettingsGpg4winDetected"), path)
            : UiLanguage.get("gpgSettingsGpg4winMissing");
        bridgeStatus.Text = UiLanguage.get(GpgBridge.isRunning ? "gpgSettingsStatusRunning" : "gpgSettingsStatusStopped");
        // Closed loop: intent (checkbox) vs reality - never fail silently.
        if (bridgeCheck.Checked && !GpgBridge.isRunning) {
            bridgeStatus.Text = GpgBridge.lastError is { Length: > 0 }
                ? string.Format(UiLanguage.get("gpgSettingsPortInUse"), portBox.Value)
                : UiLanguage.get("gpgSettingsStateMismatch");
        }
        if (probing) {
            return;
        }
        probing = true;
        _ = Task.Run(() => {
            bool agentAlive = GpgTools.probeAgent();
            if (IsDisposed) {
                return;
            }
            agentStatus.BeginInvoke(() => {
                probing = false;
                if (!IsDisposed) {
                    agentStatus.Text = UiLanguage.get(agentAlive ? "gpgSettingsStatusRunning" : "gpgSettingsStatusStopped");
                }
            });
        });
    }

    private void apply() {
        bool gpg4winPresent = GpgTools.resolveGpg4winConnectAgent() is not null;
        if (!gpg4winPresent && (bridgeCheck.Checked || agentAutostartCheck.Checked)) {
            TrayNotifications.show("gpgAgentUnavailableTitle", "gpgAgentUnavailableBody", ToolTipIcon.Warning);
            bridgeCheck.Checked         = false;
            agentAutostartCheck.Checked = false;
            refreshStatus();
            return;
        }

        Settings.gpgBridgeEnabled         = bridgeCheck.Checked;
        Settings.gpgAgentAutostartEnabled = agentAutostartCheck.Checked;
        Settings.gpgBridgePort            = (int) portBox.Value;
        Settings.save();

        if (bridgeCheck.Checked) {
            GpgBridge.start((int) portBox.Value);
            if (!GpgBridge.isRunning) {
                TrayNotifications.show("gpgBridgeStartFailedTitle", "gpgBridgeStartFailedBody", ToolTipIcon.Warning, Settings.gpgBridgePort);
            }
        } else {
            GpgBridge.stop();
        }

        if (agentAutostartCheck.Checked) {
            GpgAgentManager.startIfEnabled();
        } else {
            GpgAgentManager.stop();
        }
        refreshStatus();
    }
}

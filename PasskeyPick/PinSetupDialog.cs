using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>Modal dialog that collects the USB security key's FIDO2 PIN once (typed, never shown on a command line) and
/// caches it in memory via <see cref="PinCache"/> (never written to disk). Refuses to store a PIN when more than one
/// security key is attached, to avoid feeding the wrong key and locking it out. Grouped layout matching the GPG
/// settings dialog: an "Enter PIN" section with a right-aligned two-column grid and a "Cache status" section.</summary>
internal sealed class PinSetupDialog: BaseDialog {

    private readonly TextBox pinBox     = new() { UseSystemPasswordChar = true, Width = 220 };
    private readonly TextBox confirmBox = new() { UseSystemPasswordChar = true, Width = 220 };
    private readonly Label   ttlLabel   = new() { AutoSize = true, ForeColor = System.Drawing.SystemColors.GrayText };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };

    public PinSetupDialog() {
        Text            = $"{Startup.PROGRAM_NAME} - {UiLanguage.get("pinDialogTitle")}";
        FormClosed      += (_, _) => refreshTimer.Dispose();

        var save   = new Button { Text = UiLanguage.get("pinDialogCacheButton"), DialogResult = DialogResult.OK, Enabled = false };
        var clear  = new Button { Text = UiLanguage.get("pinDialogClearButton") };
        var cancel = new Button { Text = UiLanguage.get("pinDialogCancel"), DialogResult = DialogResult.Cancel };

        save.Click += (_, _) => {
            if (pinBox.Text.Length == 0) {
                MessageBox.Show(UiLanguage.get("pinDialogEmpty"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!string.Equals(pinBox.Text, confirmBox.Text, StringComparison.Ordinal)) {
                MessageBox.Show(UiLanguage.get("pinDialogMismatch"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Caching one PIN to auto-fill several keys could lock them out after repeated wrong attempts.
            if (Fido2Devices.countFido2() > 1) {
                MessageBox.Show(UiLanguage.get("pinDialogMultiKey"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!PinCache.set(pinBox.Text)) {
                MessageBox.Show(UiLanguage.get("pinDialogError"), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        clear.Click += (_, _) => {
            PinCache.clear();
            DialogResult = DialogResult.OK;
            Close();
        };
        pinBox.TextChanged += (_, _) => save.Enabled = pinBox.Text.Length > 0;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildInputGroup(), 0, row++);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildStatusGroup(), 0, row++);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(buildButtonRow(cancel, clear, save), 0, row);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;

        updateTtlHint();
        refreshTimer.Tick += (_, _) => updateTtlHint();
        refreshTimer.Start();
    }

    /// <summary>The "Enter PIN" section: PIN and confirm fields in a right-aligned two-column grid.</summary>
    private GroupBox buildInputGroup() {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("pinDialogPin")), 0, row);
        pinBox.Anchor = AnchorStyles.Left;
        layout.Controls.Add(pinBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildFieldLabel(UiLanguage.get("pinDialogConfirm")), 0, row);
        confirmBox.Anchor = AnchorStyles.Left;
        layout.Controls.Add(confirmBox, 1, row);

        return buildGroup(UiLanguage.get("pinDialogInputGroup"), layout);
    }

    /// <summary>The "Cache status" section: the memory-only hint and the TTL countdown.</summary>
    private GroupBox buildStatusGroup() {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = UiLanguage.get("pinDialogHint"), AutoSize = true, ForeColor = System.Drawing.SystemColors.GrayText }, 0, 0);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(ttlLabel, 0, 1);
        return buildGroup(UiLanguage.get("pinDialogStatusGroup"), layout);
    }

    private void updateTtlHint() {
        int? remaining = PinCache.remainingSeconds();
        string text;
        if (remaining is int.MaxValue) {
            text = UiLanguage.get("pinDialogTtlUnlimited");
        } else if (remaining is > 0) {
            text = string.Format(UiLanguage.get("pinDialogTtlRemaining"), formatRemaining(remaining.Value));
        } else {
            // Nothing cached (or just expired): show how long the next cached PIN will last.
            int configured = PinCache.ttlSecondsValue;
            text = configured > 0 ? string.Format(UiLanguage.get("pinDialogTtl"), configured)
                                  : UiLanguage.get("pinDialogTtlUnlimited");
        }
        ttlLabel.Text = text;
    }

    private static string formatRemaining(int seconds) {
        TimeSpan t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int) t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

}

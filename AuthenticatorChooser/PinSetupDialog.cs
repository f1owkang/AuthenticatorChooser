using System.Windows.Forms;

namespace AuthenticatorChooser;

/// <summary>Modal dialog that collects the USB security key's FIDO2 PIN once (typed, never shown on a command line) and
/// caches it in memory via <see cref="PinCache"/> (never written to disk). Refuses to store a PIN when more than one
/// security key is attached, to avoid feeding the wrong key and locking it out.</summary>
internal sealed class PinSetupDialog: Form {

    private readonly TextBox pinBox     = new() { UseSystemPasswordChar = true };
    private readonly TextBox confirmBox = new() { UseSystemPasswordChar = true };
    private readonly Label   ttlLabel   = new() { AutoSize = true, ForeColor = System.Drawing.SystemColors.GrayText };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };

    public PinSetupDialog() {
        Text            = $"{Startup.PROGRAM_NAME} - {UiLanguage.get("pinDialogTitle")}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        AutoSize        = true;
        AutoSizeMode    = AutoSizeMode.GrowAndShrink;
        Padding         = new Padding(14);
        FormClosed      += (_, _) => refreshTimer.Dispose();

        // Vertical layout: each label sits above its field in a single column; the height auto-sizes.
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;
        row = addField(layout, UiLanguage.get("pinDialogPin"), pinBox, row);
        row = addField(layout, UiLanguage.get("pinDialogConfirm"), confirmBox, row);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label {
            Text = UiLanguage.get("pinDialogHint"),
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText
        }, 0, row);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(ttlLabel, 0, row + 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var save    = new Button { Text = UiLanguage.get("pinDialogCacheButton"), DialogResult = DialogResult.OK, Enabled = false };
        var clear   = new Button { Text = UiLanguage.get("pinDialogClearButton") };
        var cancel  = new Button { Text = UiLanguage.get("pinDialogCancel"), DialogResult = DialogResult.Cancel };

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

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(save);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buttons, 0, row + 2);

        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;

        updateTtlHint();
        refreshTimer.Tick += (_, _) => updateTtlHint();
        refreshTimer.Start();
    }

    private static int addField(TableLayoutPanel layout, string label, Control field, int row) {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        layout.Controls.Add(field, 0, row + 1);
        return row + 2;
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

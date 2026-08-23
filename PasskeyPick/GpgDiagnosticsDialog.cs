using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>Shows the copyable GPG diagnostics report (issue #8). Collection runs on a background task so the UI stays
/// responsive; Copy puts the whole report on the clipboard.</summary>
internal sealed class GpgDiagnosticsDialog: BaseDialog {

    private readonly TextBox reportBox = new() {
        ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
        Width = 720, Height = 420
    };

    public GpgDiagnosticsDialog() {
        // Window title follows the PIN cache dialog convention (PasskeyPick - <title>) for a uniform look.
        Text = $"{Startup.PROGRAM_NAME} - {UiLanguage.get("gpgDiagnosticsTitle")}";

        var copy    = new Button { Text = UiLanguage.get("gpgDiagnosticsCopy"),    AutoSize = true };
        var refresh = new Button { Text = UiLanguage.get("gpgDiagnosticsRefresh"), AutoSize = true };
        var close   = new Button { Text = UiLanguage.get("gpgDiagnosticsClose"),   AutoSize = true, DialogResult = DialogResult.Cancel };

        copy.Click += (_, _) => {
            if (reportBox.TextLength == 0) {
                return;
            }
            try {
                Clipboard.SetText(reportBox.Text);
            } catch (Exception e) when (e is not OutOfMemoryException) {
                // another process holds the clipboard; never crash the tray app
            }
        };
        refresh.Click += (_, _) => refreshReport();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(reportBox, 0, 0);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buildButtonRow(close, copy, refresh), 0, 1);

        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
        Shown += (_, _) => refreshReport();
    }

    private void refreshReport() {
        reportBox.Text = UiLanguage.get("gpgDiagnosticsLoading");
        _ = Task.Run(() => {
            string report;
            try {
                report = GpgDiagnostics.generate();
            } catch (Exception e) when (e is not OutOfMemoryException) {
                report = $"(failed to collect diagnostics: {e.Message})";
            }
            if (IsDisposed) {
                return;
            }
            reportBox.BeginInvoke(() => {
                if (!IsDisposed) {
                    reportBox.Text = report;
                }
            });
        });
    }
}

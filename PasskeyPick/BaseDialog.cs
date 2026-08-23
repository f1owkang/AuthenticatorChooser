using System.Windows.Forms;

namespace PasskeyPick;

/// <summary>Shared visual style for every modal dialog: fixed border, centered, auto-sized, 14 px padding,
/// and a right-aligned button row. New dialogs derive from this so they render identically to PinSetupDialog.</summary>
internal abstract class BaseDialog: Form {

    protected BaseDialog() {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        AutoSize        = true;
        AutoSizeMode    = AutoSizeMode.GrowAndShrink;
        Padding         = new Padding(14);
    }

    /// <summary>Builds the right-aligned button row used by every dialog.</summary>
    protected static FlowLayoutPanel buildButtonRow(params Button[] buttons) {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        foreach (Button button in buttons) {
            panel.Controls.Add(button);
        }
        return panel;
    }

    /// <summary>Right-aligned field label for the two-column parameter grid used by every dialog.</summary>
    protected static Label buildFieldLabel(string text) =>
        new() { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 12, 0) };

    /// <summary>Wraps a content layout in a titled section group.</summary>
    protected static GroupBox buildGroup(string title, TableLayoutPanel content) {
        var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(content);
        return group;
    }
}

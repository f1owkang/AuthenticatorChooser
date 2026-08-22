using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AuthenticatorChooser;

/// <summary>Light/dark palette for the Windows 11 style tray menu, matched to the system theme once at startup.</summary>
internal sealed class Win11ColorTable: ProfessionalColorTable {

    internal static readonly bool IsDark = Win32Theme.isDarkTheme();

    internal static readonly Color MenuBackground = IsDark ? Color.FromArgb(0x20, 0x20, 0x20) : Color.FromArgb(0xF9, 0xF9, 0xF9);
    internal static readonly Color Border         = IsDark ? Color.FromArgb(0x3F, 0x3F, 0x3F) : Color.FromArgb(0xE5, 0xE5, 0xE5);
    internal static readonly Color Hover          = IsDark ? Color.FromArgb(0x2F, 0x2F, 0x2F) : Color.FromArgb(0xE9, 0xE9, 0xE9);
    internal static readonly Color Separator      = IsDark ? Color.FromArgb(0x3F, 0x3F, 0x3F) : Color.FromArgb(0xE5, 0xE5, 0xE5);
    internal static readonly Color CheckGlyph     = IsDark ? Color.FromArgb(0x60, 0xCD, 0xFF) : Color.FromArgb(0x00, 0x60, 0xCD);
    internal static readonly Color Text           = IsDark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);

    public override Color MenuStripGradientBegin => MenuBackground;
    public override Color MenuStripGradientEnd => MenuBackground;
    public override Color ToolStripGradientBegin => MenuBackground;
    public override Color ToolStripGradientEnd => MenuBackground;
    public override Color ToolStripBorder => Color.Transparent;
    public override Color ToolStripDropDownBackground => MenuBackground;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Hover;
    public override Color MenuItemPressedGradientEnd => Hover;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color SeparatorDark => Separator;
    public override Color SeparatorLight => MenuBackground;
    public override Color ImageMarginGradientBegin => MenuBackground;
    public override Color ImageMarginGradientMiddle => MenuBackground;
    public override Color ImageMarginGradientEnd => MenuBackground;
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
    public override Color CheckPressedBackground => Hover;
    public override Color ButtonSelectedHighlight => Hover;
    public override Color ButtonSelectedHighlightBorder => Color.Transparent;
    public override Color ButtonCheckedHighlight => Hover;
    public override Color ButtonCheckedHighlightBorder => Color.Transparent;

}

/// <summary>
/// Renders the tray context menu in a Windows 11 Fluent style: flat background, rounded hover highlights,
/// thin separators, and a check glyph, with colors matched to the system light/dark theme. The menu's rounded
/// corners are drawn by DWM (see <see cref="Win32Theme"/>), not by this renderer.
/// </summary>
internal sealed class Win11MenuRenderer: ToolStripProfessionalRenderer {

    internal Win11MenuRenderer(): base(new Win11ColorTable()) {
        RoundedEdges = false; // DWM draws the rounded corners
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
        using SolidBrush brush = new(Win11ColorTable.MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
        using Pen pen = new(Win11ColorTable.Border);
        e.Graphics.DrawRectangle(pen, e.AffectedBounds.X, e.AffectedBounds.Y, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        if (!e.Item.Selected && !e.Item.Pressed) {
            return;
        }
        Rectangle bounds = new(1, 1, e.Item.Width - 3, e.Item.Height - 2);
        using GraphicsPath path = roundedRectangle(bounds, 4);
        using SolidBrush   brush = new(Win11ColorTable.Hover);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
        Rectangle bg = new(e.ImageRectangle.X - 3, e.ImageRectangle.Y - 3, e.ImageRectangle.Width + 6, e.ImageRectangle.Height + 6);
        using (GraphicsPath path = roundedRectangle(bg, 4)) {
            using SolidBrush brush = new(Win11ColorTable.Hover);
            e.Graphics.FillPath(brush, path);
        }
        using Pen check = new(Win11ColorTable.CheckGlyph, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(check, [
            new PointF(e.ImageRectangle.X - 1, e.ImageRectangle.Y + e.ImageRectangle.Height / 2f),
            new PointF(e.ImageRectangle.X + e.ImageRectangle.Width / 4f, e.ImageRectangle.Y + e.ImageRectangle.Height - 1),
            new PointF(e.ImageRectangle.X + e.ImageRectangle.Width + 1, e.ImageRectangle.Y - 1)
        ]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
        using Pen pen = new(Win11ColorTable.Separator);
        int middle = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, middle, e.Item.Width - 8, middle);
    }

    private static GraphicsPath roundedRectangle(Rectangle bounds, int radius) {
        GraphicsPath path = new();
        int          d    = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

}

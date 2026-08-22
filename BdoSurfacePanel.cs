using System.Drawing.Drawing2D;

namespace BdoClient;

internal sealed class BdoSurfacePanel : Panel
{
    public int CornerRadius { get; set; } = 16;
    public Color SurfaceColor { get; set; } = UiTheme.Surface;
    public Color SurfaceBorderColor { get; set; } = UiTheme.Border;

    public BdoSurfacePanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Rounded surfaces must paint the real shell background outside their path.
        // Leaving this area untouched lets WinForms expose the default black buffer at corners.
        e.Graphics.Clear(UiTheme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = ClientRectangle;
        bounds.Width--;
        bounds.Height--;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var radius = Math.Min(UiTheme.Scale(this, CornerRadius), Math.Min(bounds.Width, bounds.Height) / 2);
        using var path = CreateRoundRect(bounds, radius);
        using var fill = new SolidBrush(SurfaceColor);
        using var border = new Pen(SurfaceBorderColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    private static GraphicsPath CreateRoundRect(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

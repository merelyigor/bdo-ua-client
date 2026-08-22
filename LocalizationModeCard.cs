using System.Drawing.Drawing2D;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal sealed class LocalizationModeCard : Control
{
    private const int CardPadding = 16;
    private const int FlagWidth = 30;
    private const int FlagHeight = 18;
    private const int FlagGap = 4;
    private const int FlagToTextGap = 12;
    private const int BadgeHorizontalPadding = 9;
    private const int BadgeVerticalPadding = 4;
    private const string InstalledBadgeText = "✓ Встановлено";

    private bool _hovered;
    private bool _selected;
    private bool _installed;

    public LocalizationMode Mode { get; }
    public string ModeSlug => Mode.Slug!;
    public event EventHandler? SelectionRequested;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    public bool IsInstalled
    {
        get => _installed;
        set
        {
            if (_installed == value) return;
            _installed = value;
            Invalidate();
        }
    }

    public LocalizationModeCard(LocalizationMode mode)
    {
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
        Tag = mode.Slug;
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = UiTheme.PanelBackground;
        ForeColor = UiTheme.PrimaryText;
        Margin = new Padding(0, 0, 0, 8);
        MinimumSize = new Size(160, 64);
        AccessibleRole = AccessibleRole.RadioButton;
        AccessibleName = DynamicModePolicy.GetDisplayName(mode);
        SetStyle(ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.Selectable, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Math.Max(160, proposedSize.Width > 0 ? proposedSize.Width : 520);
        using var titleFont = new Font(Font, FontStyle.Bold);
        using var bodyFont = new Font(Font, FontStyle.Regular);
        var parsed = LocalizationFlagParser.Parse(DynamicModePolicy.GetDisplayName(Mode));
        var textLeft = GetTextLeft(parsed.CountryCodes);
        var badgeWidth = IsInstalled ? GetInstalledBadgeSize(bodyFont).Width + 12 : 0;
        var textRight = width - CardPadding - badgeWidth;
        var textWidth = Math.Max(80, textRight - textLeft);
        var title = parsed.Title;
        var release = DynamicModePolicy.FormatReleaseLine(Mode);
        var description = string.IsNullOrWhiteSpace(Mode.Description) ? null : Mode.Description.Trim();
        var height = 16 + MeasureWrapped(title, titleFont, textWidth).Height;
        if (description != null)
            height += 4 + MeasureWrapped(description, bodyFont, textWidth).Height;
        if (!string.IsNullOrWhiteSpace(release))
            height += 4 + MeasureWrapped(release, bodyFont, textWidth).Height;
        return new Size(width, Math.Max(64, height + 16));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            Focus();
            SelectionRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
        {
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var surface = !Enabled
            ? UiTheme.DisabledSurface
            : _selected && _hovered
                ? UiTheme.ModeSelectedHoverSurface
                : _selected
                    ? UiTheme.ModeSelectedSurface
                    : _hovered
                        ? UiTheme.ModeHoverSurface
                        : UiTheme.PanelBackground;
        using var background = new SolidBrush(surface);
        e.Graphics.FillRectangle(background, bounds);

        var borderColor = !Enabled
            ? UiTheme.DisabledBorder
            : _selected
                ? UiTheme.Accent
                : UiTheme.Border;
        using var border = new Pen(borderColor, _selected ? 2 : 1);
        e.Graphics.DrawRectangle(border, bounds);

        if (_selected)
        {
            using var strip = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillRectangle(strip, 0, 0, 4, Height);
        }

        var parsed = LocalizationFlagParser.Parse(DynamicModePolicy.GetDisplayName(Mode));
        var flagGroupWidth = GetFlagGroupWidth(parsed.CountryCodes);
        var flagBounds = new Rectangle(CardPadding, Math.Max(12, (Height - FlagHeight) / 2),
            flagGroupWidth, FlagHeight);
        DrawFlags(e.Graphics, parsed.CountryCodes, flagBounds, Enabled);

        using var titleFont = new Font(Font, FontStyle.Bold);
        using var bodyFont = new Font(Font, FontStyle.Regular);
        var textLeft = GetTextLeft(parsed.CountryCodes);
        var installedBadgeSize = GetInstalledBadgeSize(bodyFont);
        var textRight = _installed
            ? Width - CardPadding - installedBadgeSize.Width - FlagToTextGap
            : Width - CardPadding;
        var textWidth = Math.Max(80, textRight - textLeft);
        var textColor = Enabled ? UiTheme.PrimaryText : UiTheme.DisabledText;
        var titleRect = new Rectangle(textLeft, 12, textWidth, Height - 16);
        TextRenderer.DrawText(e.Graphics, parsed.Title, titleFont, titleRect, textColor,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        var titleHeight = MeasureWrapped(parsed.Title, titleFont, textWidth).Height;
        var y = 12 + titleHeight + 2;
        if (!string.IsNullOrWhiteSpace(Mode.Description))
        {
            var description = Mode.Description.Trim();
            TextRenderer.DrawText(e.Graphics, description, bodyFont,
                new Rectangle(textLeft, y, textWidth, Height - y),
                Enabled ? UiTheme.SecondaryText : UiTheme.DisabledText,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += MeasureWrapped(description, bodyFont, textWidth).Height + 2;
        }

        var release = DynamicModePolicy.FormatReleaseLine(Mode);
        if (!string.IsNullOrWhiteSpace(release))
        {
            TextRenderer.DrawText(e.Graphics, release, bodyFont,
                new Rectangle(textLeft, y, textWidth, Height - y),
                Enabled ? UiTheme.SecondaryText : UiTheme.DisabledText,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        if (_installed)
        {
            var badge = new Rectangle(
                Width - CardPadding - installedBadgeSize.Width,
                Math.Max(8, (Height - installedBadgeSize.Height) / 2),
                installedBadgeSize.Width,
                installedBadgeSize.Height);
            using var badgeBrush = new SolidBrush(Enabled ? UiTheme.InstalledBadgeSurface : UiTheme.DisabledSurface);
            using var badgePen = new Pen(Enabled ? UiTheme.Success : UiTheme.DisabledBorder);
            e.Graphics.FillRectangle(badgeBrush, badge);
            e.Graphics.DrawRectangle(badgePen, badge);
            var badgeTextBounds = new Rectangle(
                badge.Left + BadgeHorizontalPadding,
                badge.Top + BadgeVerticalPadding,
                badge.Width - BadgeHorizontalPadding * 2,
                badge.Height - BadgeVerticalPadding * 2);
            TextRenderer.DrawText(e.Graphics, InstalledBadgeText, bodyFont, badgeTextBounds,
                Enabled ? UiTheme.Success : UiTheme.DisabledText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        if (Focused)
        {
            var focusBounds = Rectangle.Inflate(bounds, -5, -5);
            using var focusPen = new Pen(UiTheme.Accent, 1) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawRectangle(focusPen, focusBounds);
        }
    }

    private static Size MeasureWrapped(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(width, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

    private static int GetFlagGroupWidth(IReadOnlyList<string> codes) =>
        codes.Count == 0 ? 0 : codes.Count * FlagWidth + (codes.Count - 1) * FlagGap;

    private static int GetTextLeft(IReadOnlyList<string> codes) =>
        codes.Count == 0 ? CardPadding : CardPadding + GetFlagGroupWidth(codes) + FlagToTextGap;

    private static Size GetInstalledBadgeSize(Font font)
    {
        var textSize = TextRenderer.MeasureText(InstalledBadgeText, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
        return new Size(
            textSize.Width + BadgeHorizontalPadding * 2,
            Math.Max(24, textSize.Height + BadgeVerticalPadding * 2));
    }

    private static void DrawFlags(Graphics graphics, IReadOnlyList<string> codes, Rectangle bounds, bool enabled)
    {
        if (codes.Count == 0) return;
        var x = bounds.Left;
        foreach (var code in codes)
        {
            var flag = new Rectangle(x, bounds.Top, FlagWidth, FlagHeight);
            DrawFlag(graphics, code, flag, enabled);
            x += FlagWidth + FlagGap;
        }
    }

    private static void DrawFlag(Graphics graphics, string code, Rectangle bounds, bool enabled)
    {
        var outline = enabled ? UiTheme.Border : UiTheme.DisabledBorder;
        using var pen = new Pen(outline);
        if (code == "UA")
        {
            using var blue = new SolidBrush(Color.FromArgb(0, 91, 187));
            using var yellow = new SolidBrush(Color.FromArgb(255, 213, 48));
            var half = bounds.Height / 2;
            graphics.FillRectangle(blue, bounds.Left, bounds.Top, bounds.Width, half);
            graphics.FillRectangle(yellow, bounds.Left, bounds.Top + half, bounds.Width, bounds.Height - half);
            graphics.DrawRectangle(pen, bounds);
        }
        else if (code == "GB")
        {
            using var blue = new SolidBrush(Color.FromArgb(1, 33, 105));
            using var white = new Pen(Color.White, 5);
            using var red = new Pen(Color.FromArgb(200, 16, 46), 2);
            graphics.FillRectangle(blue, bounds);
            graphics.DrawLine(white, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            graphics.DrawLine(white, bounds.Right, bounds.Top, bounds.Left, bounds.Bottom);
            graphics.DrawLine(red, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            graphics.DrawLine(red, bounds.Right, bounds.Top, bounds.Left, bounds.Bottom);
            graphics.DrawLine(white, bounds.Left + bounds.Width / 2, bounds.Top, bounds.Left + bounds.Width / 2, bounds.Bottom);
            graphics.DrawLine(white, bounds.Left, bounds.Top + bounds.Height / 2, bounds.Right, bounds.Top + bounds.Height / 2);
            graphics.DrawLine(red, bounds.Left + bounds.Width / 2, bounds.Top, bounds.Left + bounds.Width / 2, bounds.Bottom);
            graphics.DrawLine(red, bounds.Left, bounds.Top + bounds.Height / 2, bounds.Right, bounds.Top + bounds.Height / 2);
            graphics.DrawRectangle(pen, bounds);
        }
        else
        {
            using var brush = new SolidBrush(enabled ? UiTheme.ControlBackground : UiTheme.DisabledSurface);
            graphics.FillRectangle(brush, bounds);
            graphics.DrawRectangle(pen, bounds);
            using var font = new Font("Segoe UI", 7F, FontStyle.Bold);
            TextRenderer.DrawText(graphics, code, font, bounds,
                enabled ? UiTheme.SecondaryText : UiTheme.DisabledText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

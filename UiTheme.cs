using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace BdoClient;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(18, 18, 18);
    public static readonly Color PanelBackground = Color.FromArgb(28, 28, 28);
    public static readonly Color ControlBackground = Color.FromArgb(45, 45, 45);
    public static readonly Color Accent = Color.FromArgb(200, 164, 21);
    public static readonly Color AccentHover = Color.FromArgb(220, 186, 43);
    public static readonly Color PrimaryPressed = Color.FromArgb(169, 138, 18);
    public static readonly Color PrimaryText = Color.FromArgb(240, 240, 240);
    public static readonly Color SecondaryText = Color.FromArgb(160, 160, 160);
    public static readonly Color AccentSecondarySurface = Color.FromArgb(35, 35, 35);
    public static readonly Color AccentSecondaryText = Color.FromArgb(214, 186, 73);
    public static readonly Color AccentSecondaryBorder = Color.FromArgb(117, 99, 28);
    public static readonly Color AccentSecondaryHover = Color.FromArgb(48, 43, 25);
    public static readonly Color AccentSecondaryHoverText = Color.FromArgb(231, 204, 97);
    public static readonly Color AccentSecondaryPressed = Color.FromArgb(39, 35, 19);
    public static readonly Color NeutralHover = Color.FromArgb(58, 58, 58);
    public static readonly Color NeutralText = Color.FromArgb(232, 232, 232);
    public static readonly Color NeutralHoverText = Color.FromArgb(240, 240, 240);
    public static readonly Color NeutralPressed = Color.FromArgb(36, 36, 36);
    public static readonly Color DestructiveSurface = Color.FromArgb(51, 36, 36);
    public static readonly Color DestructiveText = Color.FromArgb(255, 154, 154);
    public static readonly Color DestructiveBorder = Color.FromArgb(122, 57, 57);
    public static readonly Color DestructiveHover = Color.FromArgb(68, 41, 41);
    public static readonly Color DestructiveHoverText = Color.FromArgb(255, 176, 176);
    public static readonly Color DestructivePressed = Color.FromArgb(41, 32, 32);
    public static readonly Color DisabledSurface = Color.FromArgb(41, 41, 41);
    public static readonly Color DisabledText = Color.FromArgb(133, 133, 133);
    public static readonly Color DisabledBorder = Color.FromArgb(56, 56, 56);
    public static readonly Color Success = Color.FromArgb(0, 180, 0);
    public static readonly Color Error = Color.FromArgb(255, 68, 68);
    public static readonly Color Border = Color.FromArgb(61, 61, 61);
    public static readonly Color ModeHoverSurface = Color.FromArgb(35, 35, 35);
    public static readonly Color ModeSelectedSurface = Color.FromArgb(44, 40, 24);
    public static readonly Color ModeSelectedHoverSurface = Color.FromArgb(55, 48, 25);
    public static readonly Color InstalledBadgeSurface = Color.FromArgb(24, 52, 24);

    public static void StyleSecondaryButton(Button button)
    {
        ConfigureButton(button, ButtonVisualRole.Neutral);
    }

    public static void StylePrimaryButton(Button button)
    {
        ConfigureButton(button, ButtonVisualRole.Primary);
        button.Font = new Font(button.Font, FontStyle.Bold);
    }

    public static void StyleAccentSecondaryButton(Button button)
    {
        ConfigureButton(button, ButtonVisualRole.AccentSecondary);
    }

    public static void StyleDestructiveButton(Button button)
    {
        ConfigureButton(button, ButtonVisualRole.Destructive);
    }

    public static void RefreshButtonState(Button button)
    {
        if (ButtonStyles.TryGetValue(button, out var style))
            ApplyButtonState(button, style.Role);
    }

    private static readonly ConditionalWeakTable<Button, ButtonStyle> ButtonStyles = new();

    private static void ConfigureButton(Button button, ButtonVisualRole role)
    {
        ButtonStyle style = ButtonStyles.GetValue(button, _ => new ButtonStyle());
        style.Role = role;

        button.EnabledChanged -= Button_EnabledChanged;
        button.EnabledChanged += Button_EnabledChanged;
        button.MouseEnter -= Button_MouseEnter;
        button.MouseEnter += Button_MouseEnter;
        button.MouseLeave -= Button_MouseLeave;
        button.MouseLeave += Button_MouseLeave;
        ApplyButtonState(button, role);
    }

    private static void Button_EnabledChanged(object? sender, EventArgs e)
    {
        if (sender is Button button)
            RefreshButtonState(button);
    }

    private static void Button_MouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Enabled && ButtonStyles.TryGetValue(button, out var style))
            ApplyButtonState(button, style.Role, isHovered: true);
    }

    private static void Button_MouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button button && ButtonStyles.TryGetValue(button, out var style))
            ApplyButtonState(button, style.Role);
    }

    private static void ApplyButtonState(Button button, ButtonVisualRole role, bool isHovered = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;

        if (!button.Enabled)
        {
            button.BackColor = DisabledSurface;
            button.ForeColor = DisabledText;
            button.FlatAppearance.BorderColor = DisabledBorder;
            button.FlatAppearance.MouseOverBackColor = DisabledSurface;
            button.FlatAppearance.MouseDownBackColor = DisabledSurface;
            return;
        }

        switch (role)
        {
            case ButtonVisualRole.Primary:
                button.BackColor = Accent;
                button.ForeColor = Background;
                button.FlatAppearance.BorderColor = Accent;
                button.FlatAppearance.MouseOverBackColor = AccentHover;
                button.FlatAppearance.MouseDownBackColor = PrimaryPressed;
                break;
            case ButtonVisualRole.AccentSecondary:
                button.BackColor = AccentSecondarySurface;
                button.ForeColor = AccentSecondaryText;
                button.FlatAppearance.BorderColor = AccentSecondaryBorder;
                button.FlatAppearance.MouseOverBackColor = AccentSecondaryHover;
                button.FlatAppearance.MouseDownBackColor = AccentSecondaryPressed;
                if (isHovered)
                {
                    button.BackColor = AccentSecondaryHover;
                    button.ForeColor = AccentSecondaryHoverText;
                    button.FlatAppearance.BorderColor = Accent;
                }
                break;
            case ButtonVisualRole.Destructive:
                button.BackColor = DestructiveSurface;
                button.ForeColor = DestructiveText;
                button.FlatAppearance.BorderColor = DestructiveBorder;
                button.FlatAppearance.MouseOverBackColor = DestructiveHover;
                button.FlatAppearance.MouseDownBackColor = DestructivePressed;
                if (isHovered)
                {
                    button.BackColor = DestructiveHover;
                    button.ForeColor = DestructiveHoverText;
                    button.FlatAppearance.BorderColor = Color.FromArgb(165, 71, 71);
                }
                break;
            default:
                button.BackColor = ControlBackground;
                button.ForeColor = NeutralText;
                button.FlatAppearance.BorderColor = Color.FromArgb(71, 71, 71);
                button.FlatAppearance.MouseOverBackColor = NeutralHover;
                button.FlatAppearance.MouseDownBackColor = NeutralPressed;
                if (isHovered)
                {
                    button.BackColor = NeutralHover;
                    button.ForeColor = NeutralHoverText;
                    button.FlatAppearance.BorderColor = Color.FromArgb(86, 86, 86);
                }
                break;
        }
    }

    private sealed class ButtonStyle
    {
        public ButtonVisualRole Role { get; set; }
    }

    private enum ButtonVisualRole
    {
        Primary,
        AccentSecondary,
        Neutral,
        Destructive
    }
}

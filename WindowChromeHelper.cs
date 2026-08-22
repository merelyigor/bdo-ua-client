using System.Runtime.InteropServices;

namespace BdoClient;

internal static class WindowChromeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaBorderColor = 34;

    public static void ApplyDarkCaption(Form form)
    {
        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated)
            return;

        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            var caption = ColorToDword(UiTheme.BackgroundElevated);
            var text = ColorToDword(UiTheme.PrimaryText);
            var border = ColorToDword(UiTheme.Border);
            DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref text, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (ExternalException) { }
    }

    private static int ColorToDword(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using BdoClient.Logging;

namespace BdoClient.Update;

internal static class ForegroundWindowHelper
{
    private const int ShowNormal = 1;
    private const int WaitMilliseconds = 3000;
    private const int PollMilliseconds = 100;

    public static async Task<bool> TryBringToForegroundAsync(
        Process process,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            for (var elapsed = 0; elapsed < WaitMilliseconds; elapsed += PollMilliseconds)
            {
                if (process.HasExited)
                    break;

                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindowAsync(handle, ShowNormal);
                    var focused = SetForegroundWindow(handle);
                    logger.Info($"Self-update: foreground handoff {(focused ? "succeeded" : "was rejected")} (hwnd=0x{handle.ToInt64():X})");
                    return focused;
                }

                await Task.Delay(PollMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            logger.Warning("Self-update: foreground handoff cancelled");
            return false;
        }
        catch (Exception ex)
        {
            logger.Warning($"Self-update: foreground handoff unavailable: {ex.Message}");
            return false;
        }

        logger.Warning($"Self-update: foreground handoff timed out after {WaitMilliseconds}ms");
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
}

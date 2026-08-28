using System.Drawing;
using System.Windows.Forms;

namespace BdoClient;

public partial class MainForm
{
    private NotifyIcon _notifyIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private bool _explicitExitRequested;
    private Icon? _trayIcon;

    private void InitializeTray()
    {
        _trayMenu = new ContextMenuStrip(components!);

        var openItem = new ToolStripMenuItem("Відкрити");
        openItem.Click += (_, _) => RestoreFromTray();

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("Вихід");
        exitItem.Click += (_, _) => ExitFromTray();

        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(separator);
        _trayMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon(components!)
        {
            ContextMenuStrip = _trayMenu,
            Text = "BDO UA Client",
            Visible = false
        };

        try
        {
            _trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            _trayIcon = null;
        }

        _notifyIcon.Icon = _trayIcon ?? SystemIcons.Application;

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        _notifyIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        if (IsDisposed || Disposing) return;

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        ShowInTaskbar = true;
        Show();
        Activate();
        BringToFront();

        _notifyIcon.Visible = false;

        BeginInvoke(new Action(ReconcileLayoutAfterRestore));
    }

    private void ReconcileLayoutAfterRestore()
    {
        if (IsDisposed || Disposing || _closing) return;

        RefreshModeCardLayout();
        ScheduleContentFit();
    }

    private void ExitFromTray()
    {
        _explicitExitRequested = true;
        Close();
    }

    private void PrepareTrayForShutdown()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}

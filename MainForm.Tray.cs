using System.Drawing;
using System.Windows.Forms;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{
    private NotifyIcon _notifyIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _autostartMenuItem = null!;
    private bool _explicitExitRequested;
    private Icon? _trayIcon;

    private readonly WindowsAutostartService _autostartService;
    private readonly bool _startInBackground;
    private readonly SingleInstanceCoordinator? _singleInstanceCoordinator;

    private void InitializeTray()
    {
        if (_startInBackground)
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }

        _trayMenu = new ContextMenuStrip(components!);

        var openItem = new ToolStripMenuItem("Відкрити");
        openItem.Click += (_, _) => RestoreFromTray();

        var checkNowItem = new ToolStripMenuItem("Перевірити зараз");
        checkNowItem.Click += (_, _) =>
        {
            _poller.RequestImmediatePoll();
            RequestApplicationUpdateCheck();
        };

        _autostartMenuItem = new ToolStripMenuItem("Запускати разом із Windows");
        _autostartMenuItem.Click += (_, _) => ToggleAutostartFromTray();

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("Вихід");
        exitItem.Click += (_, _) => ExitFromTray();

        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(checkNowItem);
        _trayMenu.Items.Add(_autostartMenuItem);
        _trayMenu.Items.Add(separator);
        _trayMenu.Items.Add(exitItem);

        _trayMenu.Opening += (_, _) => RefreshAutostartMenuItem();

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

        // Registered before MainForm_Shown so the form is moved to tray first on
        // background startup, while the normal async startup pipeline still runs after.
        this.Shown += TrayStartup_Shown;
    }

    private void TrayStartup_Shown(object? sender, EventArgs e)
    {
        if (!_startInBackground)
            return;

        _logger.Info("Starting in background mode.");
        HideToTray();
    }

    private void HideToTray()
    {
        _notifyIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();

        _poller.SetPollingMode(ReleaseFeedPollingMode.Background);

        // T4: only starts the local monitor if a baseline already exists from a prior
        // successful state refresh. It must NOT re-baseline from the current file here.
        StartLocalFileMonitorIfEligible();
    }

    private void ObserveLocalizationNotification(LocalizationState state)
    {
        var canNotify = !Visible
            && _notifyIcon.Visible
            && !_closing
            && !_updateHandoffInProgress
            && !IsDisposed
            && !Disposing;

        if (!_localizationNotificationTracker.Observe(state, canNotify))
            return;

        try
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                "BDO UA Client",
                "Доступне оновлення української локалізації.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to show localization notification: {ex.Message}");
        }
    }

    private void ObserveApplicationUpdateNotification(string? candidateTag)
    {
        var canNotify = !Visible
            && _notifyIcon.Visible
            && !_closing
            && !_updateHandoffInProgress
            && !IsDisposed
            && !Disposing;

        if (!_applicationUpdateNotificationTracker.Observe(candidateTag, canNotify))
            return;

        try
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                "BDO UA Client",
                $"Доступна нова версія BDO UA Client {candidateTag}. Відкрийте програму, щоб оновитися.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to show application update notification: {ex.Message}");
        }
    }

    private void RegisterSecondaryActivationListener()
    {
        if (_singleInstanceCoordinator == null) return;
        if (!_singleInstanceCoordinator.IsPrimary) return;

        // The wait callback runs on a worker thread; marshal to the UI thread and
        // reuse the existing tray restore path rather than duplicating show logic.
        _singleInstanceCoordinator.RegisterActivationCallback(() =>
            BeginInvoke(new Action(TryActivateFromSecondaryInstance)));
    }

    private void TryActivateFromSecondaryInstance()
    {
        if (IsDisposed || Disposing || _closing) return;
        if (_updateHandoffInProgress) return;

        // Restore the existing MainForm without cancelling any active operation.
        RestoreFromTray();
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

        // T4: stop the periodic monitor but preserve the committed baseline so a change
        // that occurred while hidden remains comparable after restore.
        StopLocalFileMonitorPreservingBaseline();

        _poller.SetPollingMode(ReleaseFeedPollingMode.Visible);
        _poller.RequestImmediatePoll();
        RequestApplicationUpdateCheck();

        BeginInvoke(new Action(ReconcileLayoutAfterRestore));
        ScheduleLocalFileCheckAfterRestore();
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
        DisposeApplicationUpdateTimer();
        DisposeLocalFileMonitor();

        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void RefreshAutostartMenuItem()
    {
        try
        {
            _autostartMenuItem.Checked = _autostartService.IsEnabled();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to read autostart state: {ex.Message}");
        }
    }

    private async void ToggleAutostartFromTray()
    {
        try
        {
            bool current = _autostartService.IsEnabled();
            if (current)
                _autostartService.Disable();
            else
                _autostartService.Enable();

            bool actual = _autostartService.IsEnabled();
            _autostartMenuItem.Checked = actual;
            _logger.Info(actual ? "Autostart enabled from tray." : "Autostart disabled from tray.");

            await MarkAutostartPromptDismissedAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to change autostart setting: {ex.Message}");
            RefreshAutostartMenuItem();
            MessageBox.Show(
                "Не вдалося змінити параметр автозапуску.",
                "Автозапуск",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ScheduleAutostartOfferAfterManualHide()
    {
        if (_operationInProgress)
            return;

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || Disposing || _closing)
                return;
            if (_operationInProgress)
                return;
            if (Visible)
                return;

            OfferAutostartIfEligible();
        }));
    }

    private async void OfferAutostartIfEligible()
    {
        try
        {
            if (_autostartService.IsEnabled())
                return;

            var load = _configStore.Load();
            if (load.Status == FileLoadStatus.Invalid)
            {
                _logger.Warning("Config invalid; skipping autostart prompt.");
                return;
            }

            if (load.Value != null && load.Value.AutostartPromptDismissed)
                return;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Autostart prompt eligibility check failed: {ex.Message}");
            return;
        }

        var result = MessageBox.Show(
            "Запускати BDO UA Client разом із Windows?\nПрограма автоматично запускатиметься у фоновому режимі в області сповіщень.",
            "Автозапуск",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
            await AcceptAutostartPromptAsync();
        else
            await DismissAutostartPromptAsync();
    }

    private async Task AcceptAutostartPromptAsync()
    {
        try
        {
            _autostartService.Enable();

            if (_autostartService.IsEnabled())
            {
                _logger.Info("Autostart enabled via prompt.");
                RefreshAutostartMenuItem();
                await MarkAutostartPromptDismissedAsync();
            }
            else
            {
                _logger.Warning("Autostart enable did not take effect.");
                ShowAutostartWarning("Не вдалося увімкнути автозапуск.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to enable autostart: {ex.Message}");
            ShowAutostartWarning("Не вдалося увімкнути автозапуск.");
        }
    }

    private async Task DismissAutostartPromptAsync()
    {
        try
        {
            await MarkAutostartPromptDismissedAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to save autostart dismissal: {ex.Message}");
            ShowAutostartWarning("Не вдалося зберегти вибір автозапуску.");
        }
    }

    private async Task MarkAutostartPromptDismissedAsync()
    {
        var load = _configStore.Load();
        if (load.Status == FileLoadStatus.Invalid)
        {
            _logger.Warning("Config invalid; not saving autostart prompt dismissal.");
            return;
        }

        var config = load.Value ?? new Config();
        if (config.AutostartPromptDismissed)
            return;

        config.AutostartPromptDismissed = true;
        await _configStore.SaveAsync(config);
    }

    private void ShowAutostartWarning(string message)
    {
        MessageBox.Show(message, "Автозапуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

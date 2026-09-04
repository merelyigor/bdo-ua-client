using System;
using System.Windows.Forms;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{
    // --- Local file-change monitor (T4) ---

    private System.Windows.Forms.Timer? _localFileMonitorTimer;
    private readonly LocalFileChangeTracker _localFileChangeTracker = new();
    private bool _localFileCheckInProgress;

    private const int LocalFileMonitorIntervalMilliseconds = 300000; // ~5 minutes, hidden/background only

    private string? GetCurrentLocalizationFilePath()
        => _gameRoot == null ? null : GamePaths.GetLocalizationFilePath(_gameRoot);

    private bool HasValidApiManagedMetadata()
    {
        var load = _stateStore.Load();
        return load.Status == FileLoadStatus.Valid
            && load.Value?.Source == InstallationSource.Api;
    }

    /// <summary>
    /// Stops the periodic timer and forgets any committed baseline. Used when the game
    /// root becomes invalid so an old path baseline never survives into a no-game state.
    /// </summary>
    private void ClearLocalFileTracking()
    {
        _localFileMonitorTimer?.Stop();
        _localFileChangeTracker.Clear();
    }

    private void EnsureLocalFileMonitorTimer()
    {
        if (_localFileMonitorTimer != null)
            return;

        _localFileMonitorTimer = new System.Windows.Forms.Timer
        {
            Interval = LocalFileMonitorIntervalMilliseconds
        };
        _localFileMonitorTimer.Tick += LocalFileMonitorTimer_Tick;
    }

    /// <summary>
    /// Starts the monitor only when hidden/background, idle, and a baseline from a prior
    /// successful state resolution already exists. It never re-baselines from the current
    /// file, so it must not be used to "arm" a fresh baseline.
    /// </summary>
    private void StartLocalFileMonitorIfEligible()
    {
        if (_closing || IsDisposed || Disposing)
            return;

        // Negative eligibility: stop (but preserve the committed baseline) whenever the
        // monitor must not run. Operation/feed-blocked and baseline-absence are intentionally
        // NOT negative conditions — they are transient safety gates owned by the checker, so
        // the timer stays alive across hidden operations and background-startup recovery.
        if (Visible || _gameRoot == null || !HasValidApiManagedMetadata())
        {
            StopLocalFileMonitorPreservingBaseline();
            return;
        }

        var path = GetCurrentLocalizationFilePath();
        if (path == null)
        {
            StopLocalFileMonitorPreservingBaseline();
            return;
        }

        EnsureLocalFileMonitorTimer();
        if (_localFileMonitorTimer != null && !_localFileMonitorTimer.Enabled)
        {
            _localFileMonitorTimer.Start();
            _logger.Debug("Local file-change monitor started (hidden/background).");
        }
    }

    private void StopLocalFileMonitorPreservingBaseline()
    {
        if (_localFileMonitorTimer?.Enabled == true)
        {
            _localFileMonitorTimer.Stop();
            _logger.Debug("Local file-change monitor stopped (baseline preserved).");
        }
    }

    private void DisposeLocalFileMonitor()
    {
        if (_localFileMonitorTimer != null)
        {
            _localFileMonitorTimer.Stop();
            _localFileMonitorTimer.Tick -= LocalFileMonitorTimer_Tick;
            _localFileMonitorTimer.Dispose();
            _localFileMonitorTimer = null;
        }

        _localFileChangeTracker.Clear();
    }

    private void LocalFileMonitorTimer_Tick(object? sender, EventArgs e)
    {
        _ = RunLocalFileCheckSafeAsync(allowVisible: false);
    }

    /// <summary>
    /// Event-boundary wrapper for the fire-and-forget local-check Task. Catches any
    /// exception that escapes before the inner checker's own try/catch (e.g. a synchronous
    /// failure during pre-await eligibility/capture), preventing an unobserved Task fault.
    /// </summary>
    private async Task RunLocalFileCheckSafeAsync(bool allowVisible)
    {
        try
        {
            await CheckLocalFileForChangesAsync(allowVisible);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Local file change check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shared local-change detection flow used by both the periodic timer (hidden only)
    /// and the restore check (allows visible). On change, it serializes against the API
    /// feed application via the existing FeedApplicationCoordinator so the two never run
    /// a state refresh concurrently.
    /// </summary>
    private async Task CheckLocalFileForChangesAsync(bool allowVisible)
    {
        if (_localFileCheckInProgress)
            return;
        if (_closing || IsDisposed || Disposing)
            return;
        if (!allowVisible && Visible)
            return;
        if (_gameRoot == null)
            return;
        if (_operationInProgress)
            return;
        if (_feedCoordinator.IsBlocked)
            return;

        var path = GetCurrentLocalizationFilePath();
        if (path == null)
            return;
        if (!HasValidApiManagedMetadata())
            return;

        if (_feedCoordinator.IsApplying)
        {
            _logger.Debug("Local file change check deferred: API feed application in progress.");
            return;
        }

        if (!LocalizationFileFingerprint.TryCapture(path, out var current, out var captureError))
        {
            _logger.Warning($"Local file fingerprint capture failed: {captureError}");
            return;
        }

        // With a committed baseline for this exact path, only a real fingerprint change
        // requires reconciliation. Without a baseline we cannot prove the current file
        // matches the displayed state, so one RefreshStateAsync establishes it. We never
        // adopt the current fingerprint silently; the baseline is committed only by the
        // existing RefreshStateAsync integration after a successful resolution.
        bool hasBaseline = _localFileChangeTracker.HasBaselineFor(path);
        if (hasBaseline)
        {
            if (!_localFileChangeTracker.HasChanged(path, current))
                return;
            _logger.Info("Localization file fingerprint changed; refreshing state.");
        }
        else
        {
            _logger.Info("Local file monitor baseline unavailable; refreshing state to establish it.");
        }

        _localFileCheckInProgress = true;
        try
        {
            if (_feedCoordinator.IsBlocked || _feedCoordinator.IsApplying)
            {
                _logger.Debug("Local file change check deferred: feed coordinator busy.");
                return;
            }

            _feedCoordinator.BlockUpdates();
            try
            {
                await RefreshStateAsync();
                await _feedCoordinator.ApplyPendingIfAnyAsync();
            }
            finally
            {
                _feedCoordinator.UnblockUpdates();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Local file change state refresh failed: {ex.Message}");
        }
        finally
        {
            _localFileCheckInProgress = false;
        }
    }

    /// <summary>
    /// Schedules one cheap local-file comparison after tray restore. The form is already
    /// visible, so a change that occurred while hidden cannot stay stale until the next
    /// 5-minute monitor tick. Does not run during an active operation.
    /// </summary>
    private void ScheduleLocalFileCheckAfterRestore()
    {
        if (_operationInProgress || _feedCoordinator.IsBlocked || _closing || IsDisposed || Disposing)
            return;

        BeginInvoke(new Action(() => _ = RunLocalFileCheckSafeAsync(allowVisible: true)));
    }
}

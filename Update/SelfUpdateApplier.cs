using System.Diagnostics;
using BdoClient.Logging;
using BdoClient.Services;

namespace BdoClient.Update;

public sealed class SelfUpdateApplier
{
    private const int ParentWaitTimeoutSeconds = 30;
    private const int ParentPollIntervalMs = 250;

    private readonly UpdateSessionStore _sessionStore;
    private readonly ILogger _logger;
    private readonly Func<string> _getCurrentProcessPath;
    private readonly Func<string, FileVersionInfo> _getFileVersionInfo;
    private readonly Func<int, bool> _isProcessRunning;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public SelfUpdateApplier(
        UpdateSessionStore sessionStore,
        ILogger logger)
        : this(sessionStore, logger,
            () => Environment.ProcessPath ?? "",
            path => FileVersionInfo.GetVersionInfo(path),
            pid => IsProcessRunningDefault(pid),
            psi => Process.Start(psi))
    {
    }

    internal SelfUpdateApplier(
        UpdateSessionStore sessionStore,
        ILogger logger,
        Func<string> getCurrentProcessPath,
        Func<string, FileVersionInfo> getFileVersionInfo,
        Func<int, bool> isProcessRunning,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _getCurrentProcessPath = getCurrentProcessPath ?? throw new ArgumentNullException(nameof(getCurrentProcessPath));
        _getFileVersionInfo = getFileVersionInfo ?? throw new ArgumentNullException(nameof(getFileVersionInfo));
        _isProcessRunning = isProcessRunning ?? throw new ArgumentNullException(nameof(isProcessRunning));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public async Task<int> RunAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Self-update apply started (session={sessionId})");

        // 1. Load prepared session
        var loadResult = _sessionStore.LoadSessionForState(sessionId, UpdateSession.StatePrepared);
        if (loadResult.Status != UpdateSessionLoadStatus.Valid || loadResult.Session == null)
        {
            _logger.Error($"Self-update apply: session not valid (status={loadResult.Status})");
            return ExitCodeInvalidArgs;
        }

        var session = loadResult.Session;

        // 2. Verify helper identity: must run from staged EXE path
        var currentPath = _getCurrentProcessPath();
        if (string.IsNullOrWhiteSpace(currentPath) || !Path.IsPathRooted(currentPath))
        {
            _logger.Error("Self-update apply: cannot determine current executable path");
            return ExitCodeInvalidArgs;
        }

        var expectedHelperPath = Path.GetFullPath(Path.Combine(_sessionStore.GetSessionDir(sessionId), "BDO-UA-Client.exe"));
        var actualHelperPath = Path.GetFullPath(currentPath);
        if (!string.Equals(actualHelperPath, expectedHelperPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error($"Self-update apply: helper identity mismatch ({actualHelperPath} != {expectedHelperPath})");
            return ExitCodeInvalidArgs;
        }

        // 3. Verify helper SHA matches staged exe
        var helperSha = await HashHelper.ComputeFileSha256Async(currentPath, cancellationToken);
        if (!string.Equals(helperSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: helper SHA mismatch ({helperSha} != {session.StagedExeSha256})");
            return ExitCodeVerificationFailed;
        }

        // 4. Verify helper version metadata
        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        if (!targetVersion.HasValue)
        {
            _logger.Error("Self-update apply: cannot parse target version");
            return ExitCodeInvalidArgs;
        }

        var expectedFileVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}.0";
        var expectedProductVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}";

        var helperVersionInfo = _getFileVersionInfo(currentPath);
        if (string.IsNullOrWhiteSpace(helperVersionInfo.FileVersion) ||
            string.IsNullOrWhiteSpace(helperVersionInfo.ProductVersion))
        {
            _logger.Error("Self-update apply: helper has no version metadata");
            return ExitCodeVerificationFailed;
        }

        if (!string.Equals(helperVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
            !string.Equals(helperVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: helper version mismatch (FileVersion={helperVersionInfo.FileVersion}, ProductVersion={helperVersionInfo.ProductVersion})");
            return ExitCodeVerificationFailed;
        }

        // 5. Derive paths
        var targetPath = Path.GetFullPath(session.TargetPath);
        var targetDir = Path.GetDirectoryName(targetPath)!;
        var targetFileName = Path.GetFileName(targetPath);
        var candidatePath = Path.Combine(targetDir, $"{targetFileName}.update-{sessionId}.new");
        var backupPath = Path.Combine(targetDir, $"{targetFileName}.update-{sessionId}.bak");

        // 6. Verify candidate exists and hash matches
        if (!File.Exists(candidatePath))
        {
            _logger.Error($"Self-update apply: candidate not found at {candidatePath}");
            return ExitCodeVerificationFailed;
        }

        var candidateSha = await HashHelper.ComputeFileSha256Async(candidatePath, cancellationToken);
        if (!string.Equals(candidateSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: candidate SHA mismatch ({candidateSha} != {session.StagedExeSha256})");
            return ExitCodeVerificationFailed;
        }

        // 7. Wait for parent process to exit
        _logger.Info($"Self-update: waiting for parent PID {session.ParentPid} (timeout={ParentWaitTimeoutSeconds}s)");
        var parentExited = await WaitForParentExitAsync(session.ParentPid, cancellationToken);

        if (!parentExited)
        {
            _logger.Error($"Self-update apply: parent PID {session.ParentPid} did not exit within {ParentWaitTimeoutSeconds}s");
            return ExitCodeParentTimeout;
        }

        // 8. Final pre-replace revalidation (reload session from disk)
        var reloadedResult = _sessionStore.LoadSessionForState(sessionId, UpdateSession.StatePrepared);
        if (reloadedResult.Status != UpdateSessionLoadStatus.Valid || reloadedResult.Session == null)
        {
            _logger.Error("Self-update apply: session invalid on reload before replace");
            return ExitCodeVerificationFailed;
        }

        var reloadedSession = reloadedResult.Session;

        // Revalidate target
        if (!File.Exists(targetPath))
        {
            _logger.Error("Self-update apply: target missing before replace");
            return ExitCodeVerificationFailed;
        }

        var currentTargetSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
        if (!string.Equals(currentTargetSha, reloadedSession.OriginalExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: target changed after preparation ({currentTargetSha} != {reloadedSession.OriginalExeSha256})");
            return ExitCodeVerificationFailed;
        }

        var currentTargetVersion = _getFileVersionInfo(targetPath);
        var currentVersion = AppVersion.TryParseCoreVersion(reloadedSession.CurrentVersion);
        if (currentVersion.HasValue &&
            !string.IsNullOrWhiteSpace(currentTargetVersion.FileVersion) &&
            !string.IsNullOrWhiteSpace(currentTargetVersion.ProductVersion))
        {
            var expectedCurrentFileVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}.0";
            var expectedCurrentProductVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}";

            if (!string.Equals(currentTargetVersion.FileVersion, expectedCurrentFileVersion, StringComparison.Ordinal) ||
                !string.Equals(currentTargetVersion.ProductVersion, expectedCurrentProductVersion, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: target version changed after preparation");
                return ExitCodeVerificationFailed;
            }
        }

        // Revalidate candidate
        if (!File.Exists(candidatePath))
        {
            _logger.Error("Self-update apply: candidate missing before replace");
            return ExitCodeVerificationFailed;
        }

        var reloadedCandidateSha = await HashHelper.ComputeFileSha256Async(candidatePath, cancellationToken);
        if (!string.Equals(reloadedCandidateSha, reloadedSession.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: candidate changed after preparation");
            return ExitCodeVerificationFailed;
        }

        // Revalidate backup must not exist
        if (File.Exists(backupPath))
        {
            _logger.Error($"Self-update apply: backup already exists at {backupPath}");
            return ExitCodeReplaceFailed;
        }

        // 9. Perform replacement using File.Replace
        try
        {
            File.Replace(candidatePath, targetPath, backupPath);
            _logger.Debug($"Self-update: File.Replace completed (candidate -> target, backup created)");
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update apply: File.Replace failed: {ex.Message}");
            await TryRecoverAsync(targetPath, backupPath, reloadedSession, cancellationToken);
            return ExitCodeReplaceFailed;
        }

        // 10. Post-replace verification
        try
        {
            // Verify target
            if (!File.Exists(targetPath))
            {
                _logger.Error("Self-update apply: target missing after File.Replace");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            var replacedSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
            if (!string.Equals(replacedSha, reloadedSession.StagedExeSha256, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: replaced target SHA mismatch ({replacedSha} != {reloadedSession.StagedExeSha256})");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            var replacedVersionInfo = _getFileVersionInfo(targetPath);
            if (string.IsNullOrWhiteSpace(replacedVersionInfo.FileVersion) ||
                string.IsNullOrWhiteSpace(replacedVersionInfo.ProductVersion))
            {
                _logger.Error("Self-update apply: replaced target has no version metadata");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            if (!string.Equals(replacedVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
                !string.Equals(replacedVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: replaced target version mismatch (FileVersion={replacedVersionInfo.FileVersion}, ProductVersion={replacedVersionInfo.ProductVersion})");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            // Verify backup
            if (!File.Exists(backupPath))
            {
                _logger.Error("Self-update apply: backup missing after File.Replace");
                return ExitCodeVerificationFailed;
            }

            var backupSha = await HashHelper.ComputeFileSha256Async(backupPath, cancellationToken);
            if (!string.Equals(backupSha, reloadedSession.OriginalExeSha256, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: backup SHA mismatch ({backupSha} != {reloadedSession.OriginalExeSha256})");
                return ExitCodeVerificationFailed;
            }

            var backupVersionInfo = _getFileVersionInfo(backupPath);
            if (currentVersion.HasValue &&
                !string.IsNullOrWhiteSpace(backupVersionInfo.FileVersion) &&
                !string.IsNullOrWhiteSpace(backupVersionInfo.ProductVersion))
            {
                var expectedBackupFileVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}.0";
                var expectedBackupProductVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}";

                if (!string.Equals(backupVersionInfo.FileVersion, expectedBackupFileVersion, StringComparison.Ordinal) ||
                    !string.Equals(backupVersionInfo.ProductVersion, expectedBackupProductVersion, StringComparison.Ordinal))
                {
                    _logger.Error($"Self-update apply: backup version mismatch (FileVersion={backupVersionInfo.FileVersion}, ProductVersion={backupVersionInfo.ProductVersion})");
                    return ExitCodeVerificationFailed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update apply: post-replace verification exception: {ex.Message}");
            await TryRecoverAsync(targetPath, backupPath, reloadedSession, cancellationToken);
            return ExitCodeVerificationFailed;
        }

        // 11. Restart new EXE (BEFORE marking applied)
        _logger.Info($"Self-update: restarting new application at {targetPath}");
        Process? newProcess = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false,
                WorkingDirectory = targetDir
            };
            newProcess = _startProcess(psi);
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: restart new failed: {ex.Message}");
            await TryRollbackAsync(targetPath, backupPath, reloadedSession, cancellationToken);
            return await RestartOldAsync(targetPath, backupPath, reloadedSession, cancellationToken);
        }

        if (newProcess == null)
        {
            _logger.Error("Self-update: restart new returned null");
            await TryRollbackAsync(targetPath, backupPath, reloadedSession, cancellationToken);
            return await RestartOldAsync(targetPath, backupPath, reloadedSession, cancellationToken);
        }

        // 12. Mark session applied ONLY after successful restart
        session.State = UpdateSession.StateApplied;
        var writeResult = _sessionStore.WriteSession(session);
        if (!writeResult.IsSuccess)
        {
            _logger.Warning("Self-update: failed to mark session as applied (process already started, continuing)");
        }

        _logger.Info("Self-update apply complete");
        return ExitCodeSuccess;
    }

    private async Task<int> RestartOldAsync(string targetPath, string backupPath, UpdateSession session, CancellationToken cancellationToken)
    {
        // Rollback already done by caller, now try to start the old app
        try
        {
            if (!File.Exists(targetPath))
            {
                _logger.Error("Self-update: cannot restart old — target missing after rollback");
                return ExitCodeRestartFailed;
            }

            // Verify rollback restored correct old version
            var restoredSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
            if (!string.Equals(restoredSha, session.OriginalExeSha256, StringComparison.Ordinal))
            {
                _logger.Error("Self-update: cannot restart old — rollback verification failed");
                return ExitCodeRestartFailed;
            }

            var targetDir = Path.GetDirectoryName(targetPath)!;
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false,
                WorkingDirectory = targetDir
            };
            var oldProcess = _startProcess(psi);
            if (oldProcess == null)
            {
                _logger.Error("Self-update: restart old returned null");
                return ExitCodeRestartFailed;
            }

            _logger.Info("Self-update: old application restarted after rollback");
            return ExitCodeSuccess;
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: restart old failed: {ex.Message}");
            return ExitCodeRestartFailed;
        }
    }

    private async Task TryRecoverAsync(string targetPath, string backupPath, UpdateSession session, CancellationToken cancellationToken)
    {
        try
        {
            // Inspect actual filesystem state
            var targetExists = File.Exists(targetPath);
            var backupExists = File.Exists(backupPath);

            if (targetExists)
            {
                var currentSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
                if (string.Equals(currentSha, session.OriginalExeSha256, StringComparison.Ordinal))
                {
                    _logger.Info("Self-update: target still has original content — no recovery needed");
                    return;
                }
            }

            if (backupExists)
            {
                await TryRollbackAsync(targetPath, backupPath, session, cancellationToken);
            }
            else
            {
                _logger.Error("Self-update: cannot recover — both target and backup missing/changed");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: recovery attempt failed: {ex.Message}");
        }
    }

    private async Task TryRollbackAsync(string targetPath, string backupPath, UpdateSession session, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                _logger.Error("Self-update: rollback failed — backup not found");
                return;
            }

            var failedNewBackupPath = targetPath + ".failed-new";
            if (File.Exists(failedNewBackupPath))
                SafeDelete(failedNewBackupPath);

            if (File.Exists(targetPath))
            {
                // Replace failed new with backup, preserving old as .failed-new
                File.Replace(backupPath, targetPath, failedNewBackupPath);
                _logger.Debug("Self-update: rollback via File.Replace completed");
            }
            else
            {
                // Target missing, move backup to target
                File.Move(backupPath, targetPath);
                _logger.Debug("Self-update: rollback via File.Move (target was missing)");
            }

            // Verify rollback
            if (File.Exists(targetPath))
            {
                var rollbackSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
                if (string.Equals(rollbackSha, session.OriginalExeSha256, StringComparison.Ordinal))
                {
                    _logger.Info("Self-update: rollback verified — original content restored");
                }
                else
                {
                    _logger.Error($"Self-update: rollback verification failed ({rollbackSha} != {session.OriginalExeSha256})");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: rollback failed: {ex.Message}");
        }
    }

    private async Task<bool> WaitForParentExitAsync(int parentPid, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(ParentWaitTimeoutSeconds);

        while (sw.Elapsed < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (!_isProcessRunning(parentPid))
            {
                _logger.Debug($"Self-update: parent PID {parentPid} exited after {sw.ElapsedMilliseconds}ms");
                return true;
            }

            try
            {
                await Task.Delay(ParentPollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        _logger.Warning($"Self-update: parent PID {parentPid} timeout after {timeout.TotalSeconds}s");
        return false;
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsProcessRunningDefault(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public const int ExitCodeSuccess = 0;
    public const int ExitCodeInvalidArgs = 1;
    public const int ExitCodeParentTimeout = 2;
    public const int ExitCodeVerificationFailed = 3;
    public const int ExitCodeReplaceFailed = 4;
    public const int ExitCodeRestartFailed = 5;
}

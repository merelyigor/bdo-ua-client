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
    private readonly Func<string, ProcessStartInfo> _processStartInfoFactory;
    private readonly Func<int, bool> _isProcessRunning;
    private readonly Func<string> _getCurrentProcessPath;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public SelfUpdateApplier(
        UpdateSessionStore sessionStore,
        ILogger logger)
        : this(sessionStore, logger,
            path => new ProcessStartInfo(path) { UseShellExecute = true },
            pid => IsProcessRunningDefault(pid),
            () => Environment.ProcessPath ?? "",
            psi => Process.Start(psi))
    {
    }

    internal SelfUpdateApplier(
        UpdateSessionStore sessionStore,
        ILogger logger,
        Func<string, ProcessStartInfo> processStartInfoFactory,
        Func<int, bool> isProcessRunning,
        Func<string> getCurrentProcessPath,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processStartInfoFactory = processStartInfoFactory ?? throw new ArgumentNullException(nameof(processStartInfoFactory));
        _isProcessRunning = isProcessRunning ?? throw new ArgumentNullException(nameof(isProcessRunning));
        _getCurrentProcessPath = getCurrentProcessPath ?? throw new ArgumentNullException(nameof(getCurrentProcessPath));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public async Task<int> RunAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Self-update apply started (session={sessionId})");

        // 1. Verify helper identity
        var currentPath = _getCurrentProcessPath();
        if (string.IsNullOrWhiteSpace(currentPath) || !Path.IsPathRooted(currentPath))
        {
            _logger.Error("Self-update apply: cannot determine current executable path");
            return ExitCodeInvalidArgs;
        }

        // 2. Load session (prepared state)
        var loadResult = _sessionStore.LoadSessionForState(sessionId, UpdateSession.StatePrepared);
        if (loadResult.Status != UpdateSessionLoadStatus.Valid || loadResult.Session == null)
        {
            _logger.Error($"Self-update apply: session not valid (status={loadResult.Status})");
            return ExitCodeInvalidArgs;
        }

        var session = loadResult.Session;

        // 3. Verify helper identity matches staged path
        var expectedHelperPath = Path.GetFullPath(session.TargetPath);
        var actualHelperPath = Path.GetFullPath(currentPath);
        if (!string.Equals(actualHelperPath, expectedHelperPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error($"Self-update apply: helper identity mismatch ({actualHelperPath} != {expectedHelperPath})");
            return ExitCodeInvalidArgs;
        }

        // 4. Verify candidate exists and hash matches staged exe
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        var targetFileName = Path.GetFileName(session.TargetPath);
        var candidatePath = Path.Combine(targetDir, $"{targetFileName}.update-{sessionId}.new");
        var backupPath = Path.Combine(targetDir, $"{targetFileName}.update-{sessionId}.bak");

        if (!File.Exists(candidatePath))
        {
            _logger.Error($"Self-update apply: candidate not found at {candidatePath}");
            return ExitCodeVerificationFailed;
        }

        var candidateSha = await HashHelper.ComputeFileSha256Async(candidatePath, cancellationToken);
        if (!string.Equals(candidateSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: candidate SHA mismatch ({candidateSha} != {session.StagedExeSha256})");
            SafeDelete(candidatePath);
            return ExitCodeVerificationFailed;
        }

        // 5. Wait for parent process to exit
        _logger.Info($"Self-update: waiting for parent PID {session.ParentPid} (timeout={ParentWaitTimeoutSeconds}s)");
        var parentExited = await WaitForParentExitAsync(session.ParentPid, cancellationToken);

        if (!parentExited)
        {
            _logger.Error($"Self-update apply: parent PID {session.ParentPid} did not exit within {ParentWaitTimeoutSeconds}s");
            SafeDelete(candidatePath);
            return ExitCodeParentTimeout;
        }

        // 6. Perform safe replace
        var targetPath = Path.GetFullPath(session.TargetPath);

        try
        {
            // Backup current target
            if (File.Exists(targetPath))
            {
                if (File.Exists(backupPath))
                    SafeDelete(backupPath);
                File.Move(targetPath, backupPath, overwrite: true);
                _logger.Debug($"Self-update: backed up current EXE to {backupPath}");
            }

            // Replace target with candidate
            File.Move(candidatePath, targetPath, overwrite: true);
            _logger.Debug($"Self-update: replaced target with candidate");
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update apply: replace failed: {ex.Message}");
            TryRollback(targetPath, backupPath);
            return ExitCodeReplaceFailed;
        }

        // 7. Verify replaced file
        try
        {
            if (!File.Exists(targetPath))
            {
                _logger.Error("Self-update apply: target missing after replace");
                TryRollback(targetPath, backupPath);
                return ExitCodeVerificationFailed;
            }

            var replacedSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
            if (!string.Equals(replacedSha, session.StagedExeSha256, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: replaced file SHA mismatch ({replacedSha} != {session.StagedExeSha256})");
                TryRollback(targetPath, backupPath);
                return ExitCodeVerificationFailed;
            }

            // Verify version metadata (skip when EXE has no version info)
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(targetPath);
            var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
            if (targetVersion.HasValue &&
                !string.IsNullOrWhiteSpace(fileVersionInfo.FileVersion) &&
                !string.IsNullOrWhiteSpace(fileVersionInfo.ProductVersion))
            {
                var expectedFileVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}.0";
                var expectedProductVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}";

                if (!string.Equals(fileVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
                    !string.Equals(fileVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
                {
                    _logger.Error($"Self-update apply: version mismatch after replace (FileVersion={fileVersionInfo.FileVersion}, ProductVersion={fileVersionInfo.ProductVersion})");
                    TryRollback(targetPath, backupPath);
                    return ExitCodeVerificationFailed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update apply: post-replace verification failed: {ex.Message}");
            TryRollback(targetPath, backupPath);
            return ExitCodeVerificationFailed;
        }

        // 8. Mark session as applied
        session.State = UpdateSession.StateApplied;
        _sessionStore.WriteSession(session);

        // 9. Cleanup candidate and backup (best-effort)
        SafeDelete(candidatePath);
        SafeDelete(backupPath);

        // 10. Restart new EXE
        _logger.Info($"Self-update: restarting new application at {targetPath}");
        try
        {
            var psi = _processStartInfoFactory(targetPath);
            _startProcess(psi);
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: restart failed: {ex.Message}");
            TryRollback(targetPath, backupPath);
            return ExitCodeRestartFailed;
        }

        _logger.Info("Self-update apply complete");
        return ExitCodeSuccess;
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

    private void TryRollback(string targetPath, string backupPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(backupPath, targetPath);
                _logger.Info("Self-update: rollback successful — restored backup");
            }
            else
            {
                _logger.Error("Self-update: rollback failed — no backup found");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: rollback failed: {ex.Message}");
        }
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

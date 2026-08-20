using System.Diagnostics;
using System.Text.RegularExpressions;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Update;

public sealed class UpdateLifecycleService
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);
    private static readonly Regex GuidDRegex = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);

    private readonly UpdateSessionStore _sessionStore;
    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;
    private readonly Func<string> _getCurrentProcessPath;
    private readonly Func<string, FileVersionInfo> _getFileVersionInfo;
    private readonly Func<DateTimeOffset> _utcNow;

    public UpdateLifecycleService(UpdateSessionStore sessionStore, AppPaths appPaths, ILogger logger)
        : this(sessionStore, appPaths, logger,
            () => Environment.ProcessPath ?? "",
            path => FileVersionInfo.GetVersionInfo(path))
    {
    }

    internal UpdateLifecycleService(
        UpdateSessionStore sessionStore,
        AppPaths appPaths, ILogger logger,
        Func<string> getCurrentProcessPath,
        Func<string, FileVersionInfo> getFileVersionInfo,
        Func<DateTimeOffset>? utcNow = null)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _getCurrentProcessPath = getCurrentProcessPath ?? throw new ArgumentNullException(nameof(getCurrentProcessPath));
        _getFileVersionInfo = getFileVersionInfo ?? throw new ArgumentNullException(nameof(getFileVersionInfo));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public void RunStartupMaintenance()
    {
        _logger.Info("Update lifecycle: startup maintenance begin");

        try
        {
            var currentPath = _getCurrentProcessPath();
            if (string.IsNullOrWhiteSpace(currentPath) || !Path.IsPathRooted(currentPath))
            {
                _logger.Warning("Update lifecycle: cannot determine current process path, skipping");
                return;
            }

            var currentExePath = Path.GetFullPath(currentPath);
            var currentExeSha = HashHelper.ComputeFileSha256(currentExePath);
            var currentVersionInfo = _getFileVersionInfo(currentExePath);

            ScanAndProcessSessions(currentExePath, currentExeSha, currentVersionInfo);
            CleanupAbandonedSessions(currentExePath, currentExeSha, currentVersionInfo);
        }
        catch (Exception ex)
        {
            _logger.Error($"Update lifecycle: startup maintenance failed: {ex.Message}");
        }

        _logger.Info("Update lifecycle: startup maintenance end");
    }

    private void ScanAndProcessSessions(string currentExePath, string currentExeSha, FileVersionInfo currentVersionInfo)
    {
        var updatesDir = _appPaths.UpdatesDir;
        if (!Directory.Exists(updatesDir))
            return;

        foreach (var sessionDir in Directory.GetDirectories(updatesDir))
        {
            var dirName = Path.GetFileName(sessionDir);
            if (!GuidDRegex.IsMatch(dirName))
                continue;

            try
            {
                ProcessSessionDir(dirName, currentExePath, currentExeSha, currentVersionInfo);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Update lifecycle: failed to process session dir {dirName}: {ex.Message}");
            }
        }
    }

    private void ProcessSessionDir(string sessionId, string currentExePath, string currentExeSha, FileVersionInfo currentVersionInfo)
    {
        var loadResult = _sessionStore.LoadSessionAnyState(sessionId);

        if (loadResult.Status == UpdateSessionLoadStatus.Missing)
            return;

        if (loadResult.Status == UpdateSessionLoadStatus.Invalid)
        {
            _logger.Warning($"Update lifecycle: session {sessionId} has malformed metadata, preserving");
            return;
        }

        var session = loadResult.Session!;

        switch (session.State)
        {
            case UpdateSession.StateApplied:
                ProcessAppliedSession(session, currentExePath, currentExeSha, currentVersionInfo);
                break;

            case UpdateSession.StatePrepared:
                ProcessPreparedSession(session, currentExePath, currentExeSha, currentVersionInfo);
                break;

            case UpdateSession.StateStaged:
                _logger.Debug($"Update lifecycle: session {sessionId} is staged, no action needed");
                break;
        }
    }

    private void ProcessAppliedSession(UpdateSession session, string currentExePath, string currentExeSha, FileVersionInfo currentVersionInfo)
    {
        _logger.Info($"Update lifecycle: processing applied session {session.SessionId}");

        var targetPath = Path.GetFullPath(session.TargetPath);
        if (!string.Equals(currentExePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning($"Update lifecycle: applied session {session.SessionId} target mismatch ({currentExePath} != {targetPath}), preserving");
            return;
        }

        if (!File.Exists(currentExePath))
        {
            _logger.Warning($"Update lifecycle: applied session {session.SessionId} target missing, preserving");
            return;
        }

        if (!string.Equals(currentExeSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Warning($"Update lifecycle: applied session {session.SessionId} SHA mismatch ({currentExeSha} != {session.StagedExeSha256}), preserving");
            return;
        }

        if (!MatchesVersion(currentVersionInfo, session.TargetVersion))
        {
            _logger.Warning($"Update lifecycle: applied session {session.SessionId} version mismatch, preserving");
            return;
        }

        _logger.Info($"Update lifecycle: applied session {session.SessionId} confirmed — cleaning up recovery files");
        if (CleanupRecoveryFiles(session))
            CleanupSessionDir(session);
        else
            _logger.Warning($"Update lifecycle: applied session {session.SessionId} retained because recovery cleanup was not fully verified");
    }

    private void ProcessPreparedSession(UpdateSession session, string currentExePath, string currentExeSha, FileVersionInfo currentVersionInfo)
    {
        _logger.Info($"Update lifecycle: processing prepared session {session.SessionId}");

        var targetPath = Path.GetFullPath(session.TargetPath);
        if (!string.Equals(currentExePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug($"Update lifecycle: prepared session {session.SessionId} target mismatch, skipping");
            return;
        }

        var isOriginalTarget = !string.IsNullOrEmpty(session.OriginalExeSha256) &&
            string.Equals(currentExeSha, session.OriginalExeSha256, StringComparison.Ordinal);

        if (isOriginalTarget)
        {
            if (MatchesVersion(currentVersionInfo, session.CurrentVersion))
            {
                _logger.Info($"Update lifecycle: prepared session {session.SessionId} — old app restarted after failed update, cleaning up");
                if (CleanupVerifiedSiblings(session))
                    CleanupSessionDir(session);
                else
                    _logger.Warning($"Update lifecycle: prepared session {session.SessionId} retained because sibling cleanup was not fully verified");
                return;
            }
        }

        var isNewTarget = !string.IsNullOrEmpty(session.StagedExeSha256) &&
            string.Equals(currentExeSha, session.StagedExeSha256, StringComparison.Ordinal);

        if (isNewTarget && MatchesVersion(currentVersionInfo, session.TargetVersion))
        {
            _logger.Info($"Update lifecycle: prepared session {session.SessionId} — new target is already running (write race), deferring");
            return;
        }

        _logger.Debug($"Update lifecycle: prepared session {session.SessionId} — ambiguous state, preserving");
    }

    private bool CleanupRecoveryFiles(UpdateSession session)
    {
        var workspace = ReplacementWorkspace.Derive(_appPaths, session.SessionId, session.TargetPath);
        var backupPath = workspace.BackupPath;

        if (!File.Exists(backupPath))
        {
            _logger.Warning($"Update lifecycle: expected backup missing at {backupPath}, preserving session");
            return false;
        }

        try
        {
            var backupSha = HashHelper.ComputeFileSha256(backupPath);
            if (!string.Equals(backupSha, session.OriginalExeSha256, StringComparison.Ordinal))
            {
                _logger.Warning($"Update lifecycle: backup SHA mismatch at {backupPath}, NOT deleting");
                return false;
            }

            File.Delete(backupPath);
            _logger.Debug($"Update lifecycle: deleted verified backup {backupPath}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update lifecycle: failed to delete backup {backupPath}: {ex.Message}");
            return false;
        }

        if (!DeleteIfVerified(workspace.FailedNewPath, session.StagedExeSha256, "failed-new"))
            return false;

        if (!DeleteIfVerified(workspace.CandidatePath, session.StagedExeSha256, "candidate"))
            return false;

        return workspace.TryDeleteOwnedFallbackWorkspace();
    }

    private bool CleanupVerifiedSiblings(UpdateSession session)
    {
        var workspace = ReplacementWorkspace.Derive(_appPaths, session.SessionId, session.TargetPath);
        if (!DeleteIfVerified(workspace.CandidatePath, session.StagedExeSha256, "candidate"))
            return false;

        if (!DeleteIfVerified(workspace.FailedNewPath, session.StagedExeSha256, "failed-new"))
            return false;

        return workspace.TryDeleteOwnedFallbackWorkspace();
    }

    private bool DeleteIfVerified(string path, string expectedSha, string fileType)
    {
        if (!File.Exists(path))
            return true;

        try
        {
            var sha = HashHelper.ComputeFileSha256(path);
            if (string.Equals(sha, expectedSha, StringComparison.Ordinal))
            {
                File.Delete(path);
                _logger.Debug($"Update lifecycle: deleted verified {fileType} {path}");
                return true;
            }
            else
            {
                _logger.Warning($"Update lifecycle: {fileType} SHA mismatch at {path}, NOT deleting");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update lifecycle: failed to delete {fileType} {path}: {ex.Message}");
            return false;
        }
    }

    private void CleanupAbandonedSessions(string currentExePath, string currentExeSha, FileVersionInfo currentVersionInfo)
    {
        var updatesDir = _appPaths.UpdatesDir;
        if (!Directory.Exists(updatesDir))
            return;

        var cutoff = _utcNow() - RetentionPeriod;

        foreach (var sessionDir in Directory.GetDirectories(updatesDir))
        {
            var dirName = Path.GetFileName(sessionDir);
            if (!GuidDRegex.IsMatch(dirName))
                continue;

            try
            {
                ProcessAbandonedSession(dirName, sessionDir, cutoff, currentExePath, currentExeSha, currentVersionInfo);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Update lifecycle: failed to process abandoned session {dirName}: {ex.Message}");
            }
        }
    }

    private void ProcessAbandonedSession(
        string sessionId,
        string sessionDir,
        DateTimeOffset cutoff,
        string currentExePath,
        string currentExeSha,
        FileVersionInfo currentVersionInfo)
    {
        var loadResult = _sessionStore.LoadSessionAnyState(sessionId);

        if (loadResult.Status == UpdateSessionLoadStatus.Missing)
            return;

        if (loadResult.Status == UpdateSessionLoadStatus.Invalid)
        {
            _logger.Debug($"Update lifecycle: abandoned session {sessionId} has malformed metadata, leaving untouched");
            return;
        }

        var session = loadResult.Session!;

        if (session.CreatedAt > cutoff)
            return;

        _logger.Info($"Update lifecycle: abandoned session {sessionId} (state={session.State}, created={session.CreatedAt:O})");

        switch (session.State)
        {
            case UpdateSession.StateStaged:
                CleanupSessionDir(session);
                break;

            case UpdateSession.StatePrepared:
                ProcessAbandonedPrepared(session, currentExePath, currentExeSha, currentVersionInfo);
                break;

            case UpdateSession.StateApplied:
                ProcessAbandonedApplied(session, currentExePath, currentExeSha, currentVersionInfo);
                break;
        }
    }

    private void ProcessAbandonedPrepared(
        UpdateSession session,
        string currentExePath,
        string currentExeSha,
        FileVersionInfo currentVersionInfo)
    {
        if (!IsCurrentProcessTarget(session, currentExePath))
        {
            _logger.Warning($"Update lifecycle: abandoned prepared session {session.SessionId} targets a foreign executable, preserving");
            return;
        }

        var targetPath = Path.GetFullPath(session.TargetPath);
        if (!File.Exists(targetPath))
        {
            _logger.Debug($"Update lifecycle: abandoned prepared session {session.SessionId} — target missing, leaving untouched");
            return;
        }

        var targetSha = currentExeSha;
        var isOriginal = !string.IsNullOrEmpty(session.OriginalExeSha256) &&
            string.Equals(targetSha, session.OriginalExeSha256, StringComparison.Ordinal);

        if (isOriginal)
        {
            _logger.Info($"Update lifecycle: abandoned prepared session {session.SessionId} — target is original, cleaning up verified siblings");
            if (MatchesVersion(currentVersionInfo, session.CurrentVersion) && CleanupVerifiedSiblings(session))
                CleanupSessionDir(session);
            else
                _logger.Warning($"Update lifecycle: abandoned prepared session {session.SessionId} retained because target/version/sibling verification failed");
        }
        else
        {
            _logger.Info($"Update lifecycle: abandoned prepared session {session.SessionId} — ambiguous target state, preserving");
        }
    }

    private void ProcessAbandonedApplied(
        UpdateSession session,
        string currentExePath,
        string currentExeSha,
        FileVersionInfo currentVersionInfo)
    {
        if (!IsCurrentProcessTarget(session, currentExePath))
        {
            _logger.Warning($"Update lifecycle: abandoned applied session {session.SessionId} targets a foreign executable, preserving");
            return;
        }

        var targetPath = Path.GetFullPath(session.TargetPath);
        if (!File.Exists(targetPath))
        {
            _logger.Debug($"Update lifecycle: abandoned applied session {session.SessionId} — target missing, leaving untouched");
            return;
        }

        var targetSha = currentExeSha;
        var isNewTarget = !string.IsNullOrEmpty(session.StagedExeSha256) &&
            string.Equals(targetSha, session.StagedExeSha256, StringComparison.Ordinal) &&
            MatchesVersion(currentVersionInfo, session.TargetVersion);

        if (isNewTarget)
        {
            _logger.Info($"Update lifecycle: abandoned applied session {session.SessionId} — target is new, finalizing");
            if (CleanupRecoveryFiles(session))
                CleanupSessionDir(session);
            else
                _logger.Warning($"Update lifecycle: abandoned applied session {session.SessionId} retained because recovery cleanup was not fully verified");
        }
        else
        {
            _logger.Info($"Update lifecycle: abandoned applied session {session.SessionId} — unexpected target state, preserving");
        }
    }

    private static bool MatchesVersion(FileVersionInfo versionInfo, string version)
    {
        var parsed = AppVersion.TryParseCoreVersion(version);
        if (!parsed.HasValue || string.IsNullOrWhiteSpace(versionInfo.FileVersion) ||
            string.IsNullOrWhiteSpace(versionInfo.ProductVersion))
            return false;

        var expectedFileVersion = $"{parsed.Value.Major}.{parsed.Value.Minor}.{parsed.Value.Build}.0";
        var expectedProductVersion = $"{parsed.Value.Major}.{parsed.Value.Minor}.{parsed.Value.Build}";
        return string.Equals(versionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) &&
            string.Equals(versionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal);
    }

    private static bool IsCurrentProcessTarget(UpdateSession session, string currentExePath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(session.TargetPath),
                currentExePath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void CleanupSessionDir(UpdateSession session)
    {
        try
        {
            _sessionStore.CleanupSession(session.SessionId);
            var workspace = ReplacementWorkspace.Derive(_appPaths, session.SessionId, session.TargetPath);
            if (!workspace.TryDeleteOwnedFallbackWorkspace())
                _logger.Warning($"Update lifecycle: fallback workspace retained for session {session.SessionId}");
            _logger.Debug($"Update lifecycle: cleaned up session directory {session.SessionId}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update lifecycle: failed to cleanup session directory {session.SessionId}: {ex.Message}");
        }
    }
}

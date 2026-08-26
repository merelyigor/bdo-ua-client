using System.Diagnostics;
using BdoClient.Logging;
using BdoClient.Services;

namespace BdoClient.Update;

public sealed class SelfUpdatePreparationService
{
    private readonly UpdateSessionStore _sessionStore;
    private readonly ILogger _logger;
    private readonly Func<string> _getCurrentProcessPath;
    private readonly Func<string, FileVersionInfo> _getFileVersionInfo;
    private readonly Func<string, string, CancellationToken, Task> _copyFileCreateNew;

    public SelfUpdatePreparationService(UpdateSessionStore sessionStore, ILogger logger)
        : this(sessionStore, logger,
            () => Environment.ProcessPath ?? "",
            path => FileVersionInfo.GetVersionInfo(path))
    {
    }

    internal SelfUpdatePreparationService(
        UpdateSessionStore sessionStore,
        ILogger logger,
        Func<string> getCurrentProcessPath,
        Func<string, FileVersionInfo> getFileVersionInfo,
        Func<string, string, CancellationToken, Task>? copyFileCreateNew = null)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _getCurrentProcessPath = getCurrentProcessPath ?? throw new ArgumentNullException(nameof(getCurrentProcessPath));
        _getFileVersionInfo = getFileVersionInfo ?? throw new ArgumentNullException(nameof(getFileVersionInfo));
        _copyFileCreateNew = copyFileCreateNew ?? HashHelper.CopyFileCreateNewAsync;
    }

    public async Task<SelfUpdatePreparationResult> PrepareAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Self-update preparation started (session={sessionId})");

        // 1. Load staged session
        var loadResult = _sessionStore.LoadSession(sessionId);
        if (loadResult.Status != UpdateSessionLoadStatus.Valid || loadResult.Session == null)
        {
            _logger.Error($"Self-update preparation failed: session not valid (status={loadResult.Status})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.SessionInvalid, "Session not valid or not in staged state");
        }

        var session = loadResult.Session;

        // 2. Verify staged EXE exists
        var stagedDir = _sessionStore.GetSessionDir(sessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");

        if (!File.Exists(stagedExePath))
        {
            _logger.Error($"Self-update preparation failed: staged EXE not found at {stagedExePath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.StagedExeMissing, "Staged executable not found");
        }

        // 3. Staged SHA matches session
        var stagedExeSha = await HashHelper.ComputeFileSha256Async(stagedExePath, cancellationToken);
        if (!string.Equals(stagedExeSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update preparation failed: staged EXE SHA mismatch ({stagedExeSha} != {session.StagedExeSha256})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.HashMismatch, "Staged executable hash mismatch");
        }

        // 4. Staged FileVersion matches target
        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        if (!targetVersion.HasValue)
        {
            _logger.Error("Self-update preparation failed: cannot parse target version");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.VersionMismatch, "Cannot parse target version");
        }

        var stagedVersionInfo = _getFileVersionInfo(stagedExePath);
        var expectedFileVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}.0";
        var expectedProductVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}";

        if (string.IsNullOrWhiteSpace(stagedVersionInfo.FileVersion) ||
            string.IsNullOrWhiteSpace(stagedVersionInfo.ProductVersion))
        {
            _logger.Error($"Self-update preparation failed: staged EXE has no version metadata");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.VersionMismatch, "Staged executable has no version metadata");
        }

        if (!string.Equals(stagedVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
            !string.Equals(stagedVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update preparation failed: staged EXE version mismatch (FileVersion={stagedVersionInfo.FileVersion}, ProductVersion={stagedVersionInfo.ProductVersion})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.VersionMismatch, "Staged executable version mismatch");
        }

        // 5. Target path is absolute
        if (!Path.IsPathRooted(session.TargetPath))
        {
            _logger.Error($"Self-update preparation failed: target path not rooted: {session.TargetPath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.TargetInvalid, "Target path is not absolute");
        }

        // 6. Target exists
        if (!File.Exists(session.TargetPath))
        {
            _logger.Error($"Self-update preparation failed: target EXE not found at {session.TargetPath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.TargetMissing, "Current executable not found");
        }

        // 7. Current process path matches session target
        var currentProcessPath = _getCurrentProcessPath();
        var normalizedCurrent = Path.GetFullPath(currentProcessPath);
        var normalizedTarget = Path.GetFullPath(session.TargetPath);
        if (!string.Equals(normalizedCurrent, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error($"Self-update preparation failed: current process path mismatch ({normalizedCurrent} != {normalizedTarget})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.TargetInvalid, "Current process path does not match session target");
        }

        // 8. Target FileVersion matches current
        var currentVersion = AppVersion.TryParseCoreVersion(session.CurrentVersion);
        if (!currentVersion.HasValue)
        {
            _logger.Error("Self-update preparation failed: cannot parse current version");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.VersionMismatch, "Cannot parse current version");
        }

        var targetVersionInfo = _getFileVersionInfo(session.TargetPath);
        var expectedCurrentFileVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}.0";
        var expectedCurrentProductVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}";

        if (string.IsNullOrWhiteSpace(targetVersionInfo.FileVersion) ||
            string.IsNullOrWhiteSpace(targetVersionInfo.ProductVersion))
        {
            _logger.Error($"Self-update preparation failed: target EXE has no version metadata");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.VersionMismatch, "Current executable has no version metadata");
        }

        if (!string.Equals(targetVersionInfo.FileVersion, expectedCurrentFileVersion, StringComparison.Ordinal) ||
            !string.Equals(targetVersionInfo.ProductVersion, expectedCurrentProductVersion, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update preparation failed: target EXE version mismatch (FileVersion={targetVersionInfo.FileVersion}, ProductVersion={targetVersionInfo.ProductVersion})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.VersionMismatch, "Current executable version mismatch");
        }

        // 9. Capture original target SHA
        var originalExeSha = await HashHelper.ComputeFileSha256Async(session.TargetPath, cancellationToken);
        _logger.Debug($"Self-update: original EXE SHA-256 = {originalExeSha}");

        // 10. Check backup collision before handoff
        var workspace = ReplacementWorkspace.Derive(_sessionStore.AppPaths, sessionId, session.TargetPath);
        workspace.EnsureDirectory();
        var backupPath = workspace.BackupPath;

        if (File.Exists(backupPath))
        {
            _logger.Error($"Self-update preparation failed: backup already exists at {backupPath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.BackupCollision, "Backup file already exists");
        }

        // 11. Create candidate in the replacement workspace (fail-closed: no overwrite via CreateNew)
        var candidatePath = workspace.CandidatePath;

        try
        {
            await _copyFileCreateNew(stagedExePath, candidatePath, cancellationToken);
            _logger.Debug($"Self-update: copied staged EXE to candidate {candidatePath}");
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070050)) // ERROR_FILE_EXISTS
        {
            _logger.Error($"Self-update preparation failed: candidate created between pre-check and open at {candidatePath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.CandidateCollision, "Candidate file was created during copy");
        }
        catch (OperationCanceledException)
        {
            SafeDelete(candidatePath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(candidatePath);
            if (IsWriteDenied(ex))
            {
                _logger.Error($"Self-update preparation failed: target directory not writable: {ex.Message}");
                return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.WriteDenied, "Target directory not writable");
            }
            _logger.Error($"Self-update preparation failed: cannot create candidate sibling: {ex.Message}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.CandidateCopyFailed, $"Cannot create candidate in target directory: {ex.Message}");
        }

        // 11. Revalidate candidate SHA
        var candidateSha = await HashHelper.ComputeFileSha256Async(candidatePath, cancellationToken);
        if (!string.Equals(candidateSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            SafeDelete(candidatePath);
            _logger.Error($"Self-update preparation failed: candidate SHA mismatch after copy ({candidateSha} != {session.StagedExeSha256})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.CandidateCopyFailed, "Candidate hash mismatch after copy");
        }

        // 12. Revalidate candidate version
        var candidateVersionInfo = _getFileVersionInfo(candidatePath);
        if (string.IsNullOrWhiteSpace(candidateVersionInfo.FileVersion) ||
            string.IsNullOrWhiteSpace(candidateVersionInfo.ProductVersion))
        {
            SafeDelete(candidatePath);
            _logger.Error($"Self-update preparation failed: candidate has no version metadata");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.CandidateCopyFailed, "Candidate has no version metadata");
        }

        if (!string.Equals(candidateVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
            !string.Equals(candidateVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
        {
            SafeDelete(candidatePath);
            _logger.Error($"Self-update preparation failed: candidate version mismatch (FileVersion={candidateVersionInfo.FileVersion}, ProductVersion={candidateVersionInfo.ProductVersion})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.CandidateCopyFailed, "Candidate version mismatch");
        }

        // 13. Mark session as prepared
        session.State = UpdateSession.StatePrepared;
        session.OriginalExeSha256 = originalExeSha;
        var writeResult = _sessionStore.WriteSession(session);
        if (!writeResult.IsSuccess)
        {
            _logger.Error($"Self-update preparation failed: cannot write prepared session");
            SafeDelete(candidatePath);
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.SessionWriteFailed, "Failed to save preparation state");
        }

        _logger.Info($"Self-update preparation complete (session={sessionId}, candidate={candidatePath})");
        return SelfUpdatePreparationResult.Success(session, candidatePath, originalExeSha);
    }

    private void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { _logger.Warning($"Failed to cleanup temporary file {Path.GetFileName(path)}: {ex.Message}"); }
    }

    private static bool IsWriteDenied(Exception ex)
    {
        return ex is UnauthorizedAccessException ||
               (ex is IOException ioEx && ioEx.HResult == unchecked((int)0x80070005));
    }
}

public enum SelfUpdatePreparationError
{
    SessionInvalid,
    StagedExeMissing,
    HashMismatch,
    VersionMismatch,
    TargetInvalid,
    TargetMissing,
    BackupCollision,
    CandidateCollision,
    CandidateCopyFailed,
    SessionWriteFailed,
    WriteDenied
}

public sealed class SelfUpdatePreparationResult
{
    public bool IsSuccess { get; }
    public UpdateSession? Session { get; }
    public string? CandidatePath { get; }
    public string? OriginalExeSha256 { get; }
    public SelfUpdatePreparationError? Error { get; }
    public string? ErrorMessage { get; }

    private SelfUpdatePreparationResult(UpdateSession session, string candidatePath, string originalExeSha256)
    {
        IsSuccess = true;
        Session = session;
        CandidatePath = candidatePath;
        OriginalExeSha256 = originalExeSha256;
    }

    private SelfUpdatePreparationResult(SelfUpdatePreparationError error, string errorMessage)
    {
        IsSuccess = false;
        Error = error;
        ErrorMessage = errorMessage;
    }

    public static SelfUpdatePreparationResult Success(UpdateSession session, string candidatePath, string originalExeSha256)
        => new(session, candidatePath, originalExeSha256);

    public static SelfUpdatePreparationResult Failure(SelfUpdatePreparationError error, string message)
        => new(error, message);
}

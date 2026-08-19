using BdoClient.Logging;
using BdoClient.Services;

namespace BdoClient.Update;

public sealed class SelfUpdatePreparationService
{
    private readonly UpdateSessionStore _sessionStore;
    private readonly ILogger _logger;

    public SelfUpdatePreparationService(UpdateSessionStore sessionStore, ILogger logger)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SelfUpdatePreparationResult> PrepareAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Self-update preparation started (session={sessionId})");

        var loadResult = _sessionStore.LoadSession(sessionId);
        if (loadResult.Status != UpdateSessionLoadStatus.Valid || loadResult.Session == null)
        {
            _logger.Error($"Self-update preparation failed: session not valid (status={loadResult.Status})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.SessionInvalid, "Session not valid or not in staged state");
        }

        var session = loadResult.Session;

        // Verify staged EXE exists and hash matches
        var stagedDir = _sessionStore.GetSessionDir(sessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");

        if (!File.Exists(stagedExePath))
        {
            _logger.Error($"Self-update preparation failed: staged EXE not found at {stagedExePath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.StagedExeMissing, "Staged executable not found");
        }

        var stagedExeSha = await HashHelper.ComputeFileSha256Async(stagedExePath, cancellationToken);
        if (!string.Equals(stagedExeSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update preparation failed: staged EXE SHA mismatch ({stagedExeSha} != {session.StagedExeSha256})");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.HashMismatch, "Staged executable hash mismatch");
        }

        // Verify target exists
        if (!File.Exists(session.TargetPath))
        {
            _logger.Error($"Self-update preparation failed: target EXE not found at {session.TargetPath}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.TargetMissing, "Current executable not found");
        }

        // Capture original target SHA
        var originalExeSha = await HashHelper.ComputeFileSha256Async(session.TargetPath, cancellationToken);
        _logger.Debug($"Self-update: original EXE SHA-256 = {originalExeSha}");

        // Copy staged EXE to target dir as unique temp sibling (candidate)
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        var targetFileName = Path.GetFileName(session.TargetPath);
        var candidatePath = Path.Combine(targetDir, $"{targetFileName}.update-{sessionId}.new");

        try
        {
            await HashHelper.CopyFileAsync(stagedExePath, candidatePath, cancellationToken);
            _logger.Debug($"Self-update: copied staged EXE to candidate {candidatePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update preparation failed: cannot create candidate sibling: {ex.Message}");
            return SelfUpdatePreparationResult.Failure(SelfUpdatePreparationError.CandidateCopyFailed, $"Cannot create candidate in target directory: {ex.Message}");
        }

        // Mark session as prepared
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

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public enum SelfUpdatePreparationError
{
    SessionInvalid,
    StagedExeMissing,
    HashMismatch,
    TargetMissing,
    CandidateCopyFailed,
    SessionWriteFailed
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

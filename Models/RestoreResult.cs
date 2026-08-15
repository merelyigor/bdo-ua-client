namespace BdoClient.Models;

public enum RestoreError
{
    InvalidGamePath,
    SourceMissing,
    SnapshotAlreadyExists,
    SnapshotCorrupted,
    BackupIo,
    OfficialDownloadFailed,
    FallbackNotAllowed,
    PatchMismatch,
    ReplaceFailed,
    VerificationFailed,
    StateSaveFailed,
    RecoveryFailed,
    RestorePointNotFound,
    RestorePointInvalid,
    StateRestoreFailed
}

public sealed class RestoreResult
{
    public bool IsSuccess { get; }
    public RestoreError? Error { get; }
    public string? ErrorMessage { get; }

    private RestoreResult(bool isSuccess, RestoreError? error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorMessage = errorMessage;
    }

    public static RestoreResult Success() => new(true, null, null);

    public static RestoreResult Failure(RestoreError error, string? message = null) =>
        new(false, error, message);
}

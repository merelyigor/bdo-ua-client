namespace BdoClient.Services;

public enum InstallError
{
    InvalidGamePath,
    InvalidRelease,
    Incompatible,
    DownloadFailed,
    OriginalSnapshotFailed,
    PreOperationStateFailed,
    BackupFailed,
    ReplaceFailed,
    VerificationFailed,
    StateSaveFailed,
    RollbackFailed
}

public sealed class InstallResult
{
    public bool IsSuccess { get; }
    public InstallError? Error { get; }
    public string? ErrorMessage { get; }

    private InstallResult(bool isSuccess, InstallError? error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorMessage = errorMessage;
    }

    public static InstallResult Success() => new(true, null, null);

    public static InstallResult Failure(InstallError error, string? message = null)
        => new(false, error, message);
}

namespace BdoClient.Update;

public enum UpdatePackageError
{
    InvalidCandidate,
    ManifestDownloadFailed,
    ManifestInvalid,
    AssetMissing,
    DownloadFailed,
    SizeMismatch,
    HashMismatch,
    PackageInvalid,
    ExecutableInvalid,
    SessionWriteFailed,
    IoError,
    Cancelled
}

public sealed class UpdatePackageResult
{
    public bool IsSuccess { get; }
    public UpdateSession? Session { get; }
    public UpdatePackageError? Error { get; }
    public string? ErrorMessage { get; }

    private UpdatePackageResult(UpdateSession session)
    {
        IsSuccess = true;
        Session = session;
        Error = null;
        ErrorMessage = null;
    }

    private UpdatePackageResult(UpdatePackageError error, string errorMessage)
    {
        IsSuccess = false;
        Session = null;
        Error = error;
        ErrorMessage = errorMessage;
    }

    public static UpdatePackageResult Success(UpdateSession session) => new(session);
    public static UpdatePackageResult Failure(UpdatePackageError error, string message) => new(error, message);
}

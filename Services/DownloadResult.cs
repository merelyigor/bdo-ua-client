namespace BdoClient.Services;

public enum DownloadError
{
    InvalidMetadata,
    Timeout,
    Network,
    Http,
    SizeMismatch,
    HashMismatch,
    Io,
    Unexpected
}

public sealed class DownloadResult
{
    public bool IsSuccess { get; }
    public string? TempFilePath { get; }
    public long? SizeBytes { get; }
    public string? Sha256 { get; }
    public DownloadError Error { get; }
    public string? ErrorMessage { get; }

    private DownloadResult(bool isSuccess, string? tempFilePath, long? sizeBytes, string? sha256,
        DownloadError error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        TempFilePath = tempFilePath;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Error = error;
        ErrorMessage = errorMessage;
    }

    public static DownloadResult Success(string tempFilePath, long sizeBytes, string sha256) =>
        new(true, tempFilePath, sizeBytes, sha256, default, null);

    public static DownloadResult SuccessWithoutHash(string tempFilePath, long sizeBytes) =>
        new(true, tempFilePath, sizeBytes, null, default, null);

    public static DownloadResult Failure(DownloadError error, string? message = null) =>
        new(false, null, null, null, error, message);
}

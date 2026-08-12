namespace BdoClient.Storage;

public sealed class FileLoadResult<T>
{
    public FileLoadStatus Status { get; }
    public T? Value { get; }
    public string? Error { get; }

    private FileLoadResult(FileLoadStatus status, T? value, string? error)
    {
        Status = status;
        Value = value;
        Error = error;
    }

    public static FileLoadResult<T> Missing() => new(FileLoadStatus.Missing, default, null);
    public static FileLoadResult<T> Valid(T value) => new(FileLoadStatus.Valid, value, null);
    public static FileLoadResult<T> Invalid(string error) => new(FileLoadStatus.Invalid, default, error);
}

public enum FileLoadStatus
{
    Missing,
    Valid,
    Invalid
}

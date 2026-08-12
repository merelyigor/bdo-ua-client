namespace BdoClient.Api;

public enum ApiErrorKind
{
    None,
    Cancelled,
    Timeout,
    Network,
    Http,
    InvalidResponse,
    Unexpected
}

public sealed class ApiResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ApiErrorKind ErrorKind { get; }
    public string? ErrorMessage { get; }

    private ApiResult(T value)
    {
        IsSuccess = true;
        Value = value;
        ErrorKind = ApiErrorKind.None;
        ErrorMessage = null;
    }

    private ApiResult(ApiErrorKind errorKind, string errorMessage)
    {
        IsSuccess = false;
        Value = default;
        ErrorKind = errorKind;
        ErrorMessage = errorMessage;
    }

    public static ApiResult<T> Success(T value) => new(value);
    public static ApiResult<T> Failure(ApiErrorKind errorKind, string errorMessage) => new(errorKind, errorMessage);
}

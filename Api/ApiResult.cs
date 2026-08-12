namespace BdoClient.Api;

public sealed class ApiResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    private ApiResult(T value)
    {
        IsSuccess = true;
        Value = value;
        ErrorMessage = null;
    }

    private ApiResult(string errorMessage)
    {
        IsSuccess = false;
        Value = default;
        ErrorMessage = errorMessage;
    }

    public static ApiResult<T> Success(T value) => new(value);
    public static ApiResult<T> Failure(string errorMessage) => new(errorMessage);

    public static ApiResult<T> FromResponse(Func<T> deserializer, Func<string> getError)
    {
        try
        {
            var result = deserializer();
            return Success(result);
        }
        catch (Exception ex)
        {
            return Failure(getError() + " " + ex.Message);
        }
    }
}

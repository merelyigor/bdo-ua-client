namespace BdoClient.Update;

public sealed class GitHubResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    private GitHubResult(T value)
    {
        IsSuccess = true;
        Value = value;
        ErrorMessage = null;
    }

    private GitHubResult(string errorMessage)
    {
        IsSuccess = false;
        Value = default;
        ErrorMessage = errorMessage;
    }

    public static GitHubResult<T> Success(T value) => new(value);
    public static GitHubResult<T> Failure(string errorMessage) => new(errorMessage);
}

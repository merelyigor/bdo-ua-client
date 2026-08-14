namespace BdoClient.Services;

public sealed class LocalizationStateResult
{
    public LocalizationState State { get; }
    public string? Error { get; }

    public LocalizationStateResult(LocalizationState state, string? error = null)
    {
        State = state;
        Error = error;
    }

    public static LocalizationStateResult Success(LocalizationState state) => new(state);

    public static LocalizationStateResult WithWarning(LocalizationState state, string error) => new(state, error);
}

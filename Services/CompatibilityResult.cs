namespace BdoClient.Services;

public sealed class CompatibilityResult
{
    public bool IsAllowed { get; }
    public string? Reason { get; }

    private CompatibilityResult(bool isAllowed, string? reason)
    {
        IsAllowed = isAllowed;
        Reason = reason;
    }

    public static CompatibilityResult Allowed() => new(true, null);

    public static CompatibilityResult Blocked(string reason) => new(false, reason);
}

namespace BdoClient.Update;

public sealed class ApplicationCommandLine
{
    public string? ApplyUpdateSessionId { get; }
    public bool IsApplyUpdateMode => ApplyUpdateSessionId != null;

    private ApplicationCommandLine(string? applyUpdateSessionId)
    {
        ApplyUpdateSessionId = applyUpdateSessionId;
    }

    public static ApplicationCommandLine Parse(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--apply-update", StringComparison.Ordinal))
            {
                var sessionId = args[i + 1];
                if (UpdateSessionStore.IsValidSessionId(sessionId))
                    return new ApplicationCommandLine(sessionId);
                return new ApplicationCommandLine(null);
            }
        }

        return new ApplicationCommandLine(null);
    }
}

namespace BdoClient.Update;

public enum CommandLineMode
{
    Normal,
    ApplyUpdate,
    InvalidApplyUpdate
}

public sealed class ApplicationCommandLine
{
    public CommandLineMode Mode { get; }
    public string? ApplyUpdateSessionId { get; }

    private ApplicationCommandLine(CommandLineMode mode, string? applyUpdateSessionId)
    {
        Mode = mode;
        ApplyUpdateSessionId = applyUpdateSessionId;
    }

    public static ApplicationCommandLine Parse(string[] args)
    {
        int applyUpdateIndex = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--apply-update", StringComparison.Ordinal))
            {
                if (applyUpdateIndex >= 0)
                    return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null);
                applyUpdateIndex = i;
            }
        }

        if (applyUpdateIndex < 0)
            return new ApplicationCommandLine(CommandLineMode.Normal, null);

        // --apply-update must be last flag with exactly one following arg
        if (applyUpdateIndex != args.Length - 2)
            return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null);

        var sessionId = args[applyUpdateIndex + 1];
        if (!UpdateSessionStore.IsValidSessionId(sessionId))
            return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null);

        return new ApplicationCommandLine(CommandLineMode.ApplyUpdate, sessionId);
    }

    public const int ExitCodeInvalidArgs = 1;
}

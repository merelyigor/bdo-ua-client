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
        // Fast path: no --apply-update anywhere → Normal
        int flagIndex = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--apply-update", StringComparison.Ordinal))
            {
                flagIndex = i;
                break;
            }
        }

        if (flagIndex < 0)
            return new ApplicationCommandLine(CommandLineMode.Normal, null);

        // Exact grammar: ["--apply-update", <canonical-guid>]
        if (args.Length != 2 || flagIndex != 0)
            return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null);

        var sessionId = args[1];
        if (!UpdateSessionStore.IsValidSessionId(sessionId))
            return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null);

        return new ApplicationCommandLine(CommandLineMode.ApplyUpdate, sessionId);
    }

    public const int ExitCodeInvalidArgs = 1;
}

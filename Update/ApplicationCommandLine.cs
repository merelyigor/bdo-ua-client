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
    public bool StartInBackground { get; }

    private ApplicationCommandLine(CommandLineMode mode, string? applyUpdateSessionId, bool startInBackground)
    {
        Mode = mode;
        ApplyUpdateSessionId = applyUpdateSessionId;
        StartInBackground = startInBackground;
    }

    public static ApplicationCommandLine Parse(string[] args)
    {
        // Fast path: no --apply-update anywhere → Normal (may include --background)
        int flagIndex = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--apply-update", StringComparison.Ordinal))
            {
                flagIndex = i;
                break;
            }
        }

        // Strict apply-update grammar. Mixing --background with --apply-update is invalid
        // and must never reach normal background mode.
        if (flagIndex >= 0)
        {
            if (args.Length != 2 || flagIndex != 0)
                return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null, false);

            var sessionId = args[1];
            if (!UpdateSessionStore.IsValidSessionId(sessionId))
                return new ApplicationCommandLine(CommandLineMode.InvalidApplyUpdate, null, false);

            return new ApplicationCommandLine(CommandLineMode.ApplyUpdate, sessionId, false);
        }

        // --background is accepted only as the exact sole argument.
        bool startInBackground = args.Length == 1
            && string.Equals(args[0], "--background", StringComparison.Ordinal);

        return new ApplicationCommandLine(CommandLineMode.Normal, null, startInBackground);
    }

    public const int ExitCodeInvalidArgs = 1;
}

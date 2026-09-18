namespace BdoClient.Storage;

/// <summary>
/// Canonical filesystem ownership for one game's persisted state.
/// The identifier is a stable application-owned segment, never user input.
/// </summary>
public sealed class GamePersistencePaths
{
    public string ApplicationRoot { get; }
    public string GameId { get; }
    public string Root { get; }
    public string CacheDir { get; }
    public string ReleaseFeedCacheFile { get; }
    public string StateDir { get; }
    public string BackupsDir { get; }
    public string OriginalBackupDir { get; }
    public string RestorePointsDir { get; }
    public string ConfigFile { get; }
    public string InstallationFile { get; }

    public GamePersistencePaths(string applicationRoot, string gameId)
    {
        if (string.IsNullOrWhiteSpace(applicationRoot))
            throw new ArgumentException("Application root is required.", nameof(applicationRoot));
        if (!IsSafeGameId(gameId))
            throw new ArgumentException("Game id must be a stable lowercase path segment.", nameof(gameId));

        ApplicationRoot = Path.GetFullPath(applicationRoot);
        GameId = gameId;
        Root = Path.Combine(ApplicationRoot, "games", GameId);
        CacheDir = Path.Combine(Root, "cache");
        ReleaseFeedCacheFile = Path.Combine(CacheDir, "release-feed.json");
        StateDir = Path.Combine(Root, "state");
        BackupsDir = Path.Combine(Root, "backups");
        OriginalBackupDir = Path.Combine(BackupsDir, "original");
        RestorePointsDir = Path.Combine(BackupsDir, "restore-points");
        ConfigFile = Path.Combine(Root, "config.json");
        InstallationFile = Path.Combine(StateDir, "installation.json");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(StateDir);
        Directory.CreateDirectory(OriginalBackupDir);
        Directory.CreateDirectory(RestorePointsDir);
    }

    private static bool IsSafeGameId(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Length > 64)
            return false;

        foreach (var character in gameId)
        {
            if ((character < 'a' || character > 'z')
                && (character < '0' || character > '9')
                && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}

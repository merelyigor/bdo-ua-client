namespace BdoClient.Storage;

public sealed class AppPaths
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string Root { get; }
    public string StateDir { get; }
    public string LogsDir { get; }
    public string CacheDir { get; }
    public string BackupsDir { get; }
    public string OriginalBackupDir { get; }
    public string RestorePointsDir { get; }
    public string UpdatesDir { get; }
    public string ConfigFile { get; }
    public string ApplicationConfigFile { get; }
    public string InstallationFile { get; }

    public AppPaths() : this(Path.Combine(LocalAppData, "BDO-UA-Client")) { }

    public AppPaths(string root)
    {
        Root = root;
        StateDir = Path.Combine(root, "state");
        LogsDir = Path.Combine(root, "logs");
        CacheDir = Path.Combine(root, "cache");
        BackupsDir = Path.Combine(root, "backups");
        OriginalBackupDir = Path.Combine(BackupsDir, "original");
        RestorePointsDir = Path.Combine(BackupsDir, "restore-points");
        UpdatesDir = Path.Combine(root, "updates");
        ConfigFile = Path.Combine(root, "config.json");
        ApplicationConfigFile = Path.Combine(root, "application-config.json");
        InstallationFile = Path.Combine(StateDir, "installation.json");
    }

    /// <summary>
    /// Creates the current global application directories. The other path
    /// properties remain the legacy single-game layout used by migration and
    /// compatibility test fixtures.
    /// </summary>
    public void EnsureGlobalDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(UpdatesDir);
    }

    public GamePersistencePaths GetGamePersistencePaths(string gameId)
    {
        return new GamePersistencePaths(Root, gameId);
    }

    // Kept for legacy-layout fixtures and compatibility callers. Production
    // composition uses EnsureGlobalDirectories plus a scoped path set.
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(StateDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(OriginalBackupDir);
        Directory.CreateDirectory(RestorePointsDir);
        Directory.CreateDirectory(UpdatesDir);
    }
}

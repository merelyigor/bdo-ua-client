using BdoClient.Storage;

namespace BdoClient.Tests.Storage;

public class AppPathsTests : IDisposable
{
    private readonly string _tempDir;

    public AppPathsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Constructor_SetsCorrectPaths()
    {
        var paths = new AppPaths(_tempDir);
        Assert.Equal(_tempDir, paths.Root);
        Assert.Equal(Path.Combine(_tempDir, "state"), paths.StateDir);
        Assert.Equal(Path.Combine(_tempDir, "logs"), paths.LogsDir);
        Assert.Equal(Path.Combine(_tempDir, "cache"), paths.CacheDir);
        Assert.Equal(Path.Combine(_tempDir, "backups"), paths.BackupsDir);
        Assert.Equal(Path.Combine(_tempDir, "backups", "original"), paths.OriginalBackupDir);
        Assert.Equal(Path.Combine(_tempDir, "backups", "restore-points"), paths.RestorePointsDir);
        Assert.Equal(Path.Combine(_tempDir, "config.json"), paths.ConfigFile);
        Assert.Equal(Path.Combine(_tempDir, "state", "installation.json"), paths.InstallationFile);
    }

    [Fact]
    public void EnsureDirectories_CreatesAllDirectories()
    {
        var paths = new AppPaths(_tempDir);
        paths.EnsureDirectories();

        Assert.True(Directory.Exists(paths.StateDir));
        Assert.True(Directory.Exists(paths.LogsDir));
        Assert.True(Directory.Exists(paths.CacheDir));
        Assert.True(Directory.Exists(paths.OriginalBackupDir));
        Assert.True(Directory.Exists(paths.RestorePointsDir));
    }

    [Fact]
    public void EnsureDirectories_IsIdempotent()
    {
        var paths = new AppPaths(_tempDir);
        paths.EnsureDirectories();
        paths.EnsureDirectories();
        paths.EnsureDirectories();

        Assert.True(Directory.Exists(paths.StateDir));
    }
}

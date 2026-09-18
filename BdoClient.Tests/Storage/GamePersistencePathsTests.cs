using BdoClient.Storage;
using BdoClient.Logging;
using BdoClient.Services;

namespace BdoClient.Tests.Storage;

public sealed class GamePersistencePathsTests
{
    [Fact]
    public void Scope_UsesStableGameNamespaceWithoutChangingApplicationGlobalPaths()
    {
        var appPaths = new AppPaths(Path.Combine(Path.GetTempPath(), "bdo-paths", Guid.NewGuid().ToString("N")));
        var blackDesert = appPaths.GetGamePersistencePaths("black-desert-online");
        var secondGame = appPaths.GetGamePersistencePaths("second-game");

        Assert.Equal(Path.Combine(appPaths.Root, "games", "black-desert-online"), blackDesert.Root);
        Assert.Equal(Path.Combine(blackDesert.Root, "cache"), blackDesert.CacheDir);
        Assert.Equal(Path.Combine(blackDesert.CacheDir, "release-feed.json"), blackDesert.ReleaseFeedCacheFile);
        Assert.Equal(Path.Combine(blackDesert.Root, "config.json"), blackDesert.ConfigFile);
        Assert.Equal(Path.Combine(blackDesert.Root, "state", "installation.json"), blackDesert.InstallationFile);
        Assert.NotEqual(blackDesert.Root, secondGame.Root);
        Assert.NotEqual(blackDesert.ReleaseFeedCacheFile, secondGame.ReleaseFeedCacheFile);
        Assert.NotEqual(blackDesert.InstallationFile, secondGame.InstallationFile);
        Assert.Equal(Path.Combine(appPaths.Root, "logs"), appPaths.LogsDir);
        Assert.Equal(Path.Combine(appPaths.Root, "cache"), appPaths.CacheDir);
        Assert.Equal(Path.Combine(appPaths.Root, "updates"), appPaths.UpdatesDir);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("BlackDesert")]
    [InlineData("game/id")]
    [InlineData("")]
    public void Scope_RejectsUnsafeGameId(string gameId)
    {
        var appPaths = new AppPaths(Path.Combine(Path.GetTempPath(), "bdo-paths", Guid.NewGuid().ToString("N")));

        Assert.Throws<ArgumentException>(() => appPaths.GetGamePersistencePaths(gameId));
    }

    [Fact]
    public async Task ScopedStores_WriteOnlyInsideTheirGameNamespace()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-scoped-stores", Guid.NewGuid().ToString("N"));
        try
        {
            var appPaths = new AppPaths(root);
            var scope = appPaths.GetGamePersistencePaths("black-desert-online");
            scope.EnsureDirectories();
            var logger = new NullLogger();

            await new ConfigStore(scope, logger).SaveAsync(new Config { GamePath = "C:\\Black Desert" });
            await new InstallationStateStore(scope, logger).SaveAsync(new InstallationMetadata
            {
                InstalledAt = DateTimeOffset.UtcNow,
                Source = InstallationSource.Official
            });

            Assert.True(File.Exists(scope.ConfigFile));
            Assert.True(Directory.Exists(scope.CacheDir));
            Assert.StartsWith(scope.Root, scope.ReleaseFeedCacheFile, StringComparison.Ordinal);
            Assert.True(File.Exists(scope.InstallationFile));
            Assert.False(File.Exists(appPaths.ConfigFile));
            Assert.False(File.Exists(appPaths.InstallationFile));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScopedBackupStore_DoesNotSeeAnotherGameRestorePoints()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-scoped-backups", Guid.NewGuid().ToString("N"));
        try
        {
            var appPaths = new AppPaths(root);
            var firstGame = appPaths.GetGamePersistencePaths("black-desert-online");
            var secondGame = appPaths.GetGamePersistencePaths("second-game");
            firstGame.EnsureDirectories();
            secondGame.EnsureDirectories();
            var sourceFile = Path.Combine(root, "source.loc");
            await File.WriteAllTextAsync(sourceFile, "game-a-localization");

            var firstStore = new BackupStore(firstGame, new NullLogger(), BdoGameDefinition.Default);
            var (_, result) = await firstStore.CreateRestorePointAsync(sourceFile, 401, "test");
            var secondPoints = await new BackupStore(secondGame, new NullLogger(), BdoGameDefinition.Default)
                .ListRestorePointsAsync();

            Assert.True(result.IsSuccess);
            Assert.Empty(secondPoints);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Tests.Storage;

public sealed class LegacyBdoReleaseFeedCacheMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "bdo-legacy-feed-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly GamePersistencePaths _bdoPaths;

    public LegacyBdoReleaseFeedCacheMigratorTests()
    {
        _paths = new AppPaths(_root);
        _bdoPaths = _paths.GetGamePersistencePaths("black-desert-online");
        _bdoPaths.EnsureDirectories();
    }

    [Fact]
    public void ValidLegacyCache_IsImportedToBdoScopeAndSourceIsRetained()
    {
        var legacyBytes = WriteLegacyCache(401, "legacy-mode");

        new LegacyBdoReleaseFeedCacheMigrator(_paths, _bdoPaths, new TestLogger())
            .ImportIfNeeded();

        Assert.True(File.Exists(LegacyCacheFile));
        Assert.True(File.Exists(_bdoPaths.ReleaseFeedCacheFile));
        Assert.Equal(legacyBytes, File.ReadAllBytes(_bdoPaths.ReleaseFeedCacheFile));
        var loaded = new ReleaseFeedCacheStore(_bdoPaths, new TestLogger()).Load();
        Assert.Equal(FileLoadStatus.Valid, loaded.Status);
        Assert.Equal(401, loaded.Value!.Data!.OfficialPatch);
        Assert.Equal("legacy-mode", loaded.Value.Data.Modes![0].Slug);
    }

    [Fact]
    public async Task ExistingScopedCache_IsAuthoritativeAndNotOverwritten()
    {
        _bdoPaths.EnsureDirectories();
        var scopedStore = new ReleaseFeedCacheStore(_bdoPaths, new TestLogger());
        Assert.True(await scopedStore.SaveAsync(CreateFeed(402, "scoped-mode")));
        var before = File.ReadAllBytes(_bdoPaths.ReleaseFeedCacheFile);
        WriteLegacyCache(401, "legacy-mode");

        new LegacyBdoReleaseFeedCacheMigrator(_paths, _bdoPaths, new TestLogger())
            .ImportIfNeeded();

        Assert.Equal(before, File.ReadAllBytes(_bdoPaths.ReleaseFeedCacheFile));
        Assert.Equal(402, scopedStore.Load().Value!.Data!.OfficialPatch);
    }

    [Fact]
    public void InvalidLegacyCache_IsIgnoredWithoutCreatingScopedState()
    {
        Directory.CreateDirectory(_paths.CacheDir);
        File.WriteAllText(LegacyCacheFile, "{not-valid-json");

        var exception = Record.Exception(() =>
            new LegacyBdoReleaseFeedCacheMigrator(_paths, _bdoPaths, new TestLogger())
                .ImportIfNeeded());

        Assert.Null(exception);
        Assert.False(File.Exists(_bdoPaths.ReleaseFeedCacheFile));
        Assert.True(File.Exists(LegacyCacheFile));
    }

    private string LegacyCacheFile => Path.Combine(_paths.CacheDir, "release-feed.json");

    private byte[] WriteLegacyCache(int patch, string slug)
    {
        Directory.CreateDirectory(_paths.CacheDir);
        var json = JsonSerializer.Serialize(new ReleaseFeedCacheSnapshot
        {
            SchemaVersion = 1,
            SavedAtUtc = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero),
            Data = new ReleaseFeedCacheData
            {
                OfficialPatch = patch,
                OfficialSourceUrl = "https://example.com/original.loc",
                Modes = new List<ReleaseFeedCacheMode>
                {
                    new()
                    {
                        Slug = slug,
                        PublicName = slug,
                        Current = new ReleaseFeedCacheCurrent
                        {
                            PublicId = "01LEGACYMODE",
                            Version = 1,
                            DownloadUrl = "https://example.com/release.loc",
                            SizeBytes = 100,
                            Sha256 = new string('a', 64),
                            Patch = patch,
                            CompatibleWithOfficialPatch = true,
                            PublishedAt = "2026-09-18T09:00:00Z"
                        }
                    }
                }
            }
        });
        File.WriteAllText(LegacyCacheFile, json);
        return File.ReadAllBytes(LegacyCacheFile);
    }

    private static ReleasesResponse CreateFeed(int patch, string slug) => new()
    {
        Success = true,
        Data = new ReleaseData
        {
            OfficialPatch = patch,
            OfficialSourceUrl = "https://example.com/original.loc",
            Modes = new List<LocalizationMode>
            {
                new()
                {
                    Slug = slug,
                    PublicName = slug,
                    Current = new CurrentRelease
                    {
                        PublicId = "01SCOPEDMODE",
                        Version = 1,
                        DownloadUrl = "https://example.com/release.loc",
                        SizeBytes = 100,
                        Sha256 = new string('b', 64),
                        Patch = patch,
                        CompatibleWithOfficialPatch = true,
                        PublishedAt = "2026-09-18T09:00:00Z"
                    }
                }
            }
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

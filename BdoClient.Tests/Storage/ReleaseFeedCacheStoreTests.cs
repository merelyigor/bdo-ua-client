using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Tests.Storage;

public sealed class ReleaseFeedCacheStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "bdo-ua-release-feed-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ReleaseFeedCacheStore _store;

    public ReleaseFeedCacheStoreTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureDirectories();
        _store = new ReleaseFeedCacheStore(_paths, new TestLogger());
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsNormalizedFeedAndUtcTimestamp()
    {
        var savedAt = new DateTimeOffset(2026, 9, 8, 12, 34, 56, TimeSpan.Zero);
        Assert.True(await _store.SaveAsync(CreateFeed(), savedAt));

        var loaded = _store.Load();
        Assert.Equal(FileLoadStatus.Valid, loaded.Status);
        Assert.Equal(savedAt, loaded.Value!.SavedAtUtc);
        Assert.Equal(1, loaded.Value.SchemaVersion);
        Assert.Single(loaded.Value.Data!.Modes!);

        Assert.True(ReleaseFeedCacheMapper.TryToLiveFeed(
            loaded.Value, out var reconstructed, out var error));
        Assert.Null(error);
        Assert.NotNull(reconstructed);
        Assert.Null(reconstructed!.Data!.InstallPathPatterns);
        Assert.Null(reconstructed.Data.Modes![0].History);
        Assert.Equal("mode-a", reconstructed.Data.Modes[0].Slug);
    }

    [Fact]
    public async Task EmptyModes_AreValidAndReconstructed()
    {
        var feed = CreateFeed();
        feed.Data!.Modes = new List<LocalizationMode>();

        Assert.True(await _store.SaveAsync(feed));
        var loaded = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, loaded.Status);
        Assert.Empty(loaded.Value!.Data!.Modes!);
    }

    [Fact]
    public void MissingCache_IsReportedWithoutCreatingData()
    {
        var loaded = _store.Load();

        Assert.Equal(FileLoadStatus.Missing, loaded.Status);
        Assert.False(File.Exists(_store.CacheFile));
    }

    [Fact]
    public async Task MalformedJson_IsInvalidAndDoesNotThrow()
    {
        await File.WriteAllTextAsync(_store.CacheFile, "{not-json");

        var loaded = _store.Load();

        Assert.Equal(FileLoadStatus.Invalid, loaded.Status);
        Assert.Contains("JSON", loaded.Error);
    }

    [Fact]
    public async Task UnsupportedSchema_IsRejected()
    {
        await File.WriteAllTextAsync(_store.CacheFile, JsonSerializer.Serialize(
            new ReleaseFeedCacheSnapshot
            {
                SchemaVersion = 999,
                SavedAtUtc = DateTimeOffset.UtcNow,
                Data = new ReleaseFeedCacheData { Modes = new() }
            }));

        var loaded = _store.Load();

        Assert.Equal(FileLoadStatus.Invalid, loaded.Status);
        Assert.Contains("Unsupported cache schema", loaded.Error);
    }

    [Fact]
    public async Task InvalidIdentity_IsRejected()
    {
        var feed = CreateFeed();
        feed.Data!.Modes![0].Current!.PublicId = "";

        Assert.False(await _store.SaveAsync(feed));
        Assert.False(File.Exists(_store.CacheFile));
    }

    [Fact]
    public async Task DuplicateModeIdentity_IsRejected()
    {
        var feed = CreateFeed();
        feed.Data!.Modes!.Add(new LocalizationMode
        {
            Slug = "mode-a",
            Current = null
        });

        Assert.False(await _store.SaveAsync(feed));
        Assert.False(File.Exists(_store.CacheFile));
    }

    [Fact]
    public async Task InvalidWrite_PreservesPreviousValidCacheAndLeavesNoTempFile()
    {
        var savedAt = DateTimeOffset.UtcNow;
        Assert.True(await _store.SaveAsync(CreateFeed(), savedAt));

        var invalid = CreateFeed();
        invalid.Data!.Modes![0].Current!.Sha256 = "bad";
        Assert.False(await _store.SaveAsync(invalid));

        var loaded = _store.Load();
        Assert.Equal(FileLoadStatus.Valid, loaded.Status);
        Assert.Equal(savedAt, loaded.Value!.SavedAtUtc);
        Assert.Empty(Directory.GetFiles(_paths.CacheDir, "release-feed.*.tmp"));
    }

    [Fact]
    public async Task HistoryAndInstallPathHints_AreNotPersisted()
    {
        var feed = CreateFeed();
        feed.Data!.InstallPathPatterns = new List<InstallPathPattern>
        {
            new() { Pattern = @"{drive}:\Games\BDO", Launcher = "steam" }
        };
        feed.Data.Modes![0].History = new List<ReleaseHistoryItem>
        {
            new() { PublicId = "old", Version = 1, Patch = 100, Status = "superseded" }
        };

        Assert.True(await _store.SaveAsync(feed));
        var json = await File.ReadAllTextAsync(_store.CacheFile);

        Assert.DoesNotContain("install_path_patterns", json);
        Assert.DoesNotContain("history", json);
    }

    private static ReleasesResponse CreateFeed() => new()
    {
        Success = true,
        Data = new ReleaseData
        {
            OfficialPatch = 100,
            OfficialSourceUrl = "https://example.com/original.loc",
            Modes = new List<LocalizationMode>
            {
                new()
                {
                    Slug = "mode-a",
                    PublicName = "Mode A",
                    Description = "Description",
                    Audience = "Everyone",
                    Current = new CurrentRelease
                    {
                        PublicId = "01MODEA",
                        Version = 2,
                        DownloadUrl = "https://example.com/mode-a.loc",
                        SizeBytes = 1024,
                        Sha256 = new string('a', 64),
                        Patch = 100,
                        CompatibleWithOfficialPatch = true,
                        PublishedAt = "2026-09-08T10:00:00Z"
                    }
                }
            }
        }
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

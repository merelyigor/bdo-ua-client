using BdoClient.Storage;
using BdoClient.Logging;

namespace BdoClient.Tests.Storage;

public class InstallationStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly InstallationStateStore _store;
    private readonly NullLogger _logger = new();

    public InstallationStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
        _store = new InstallationStateStore(_paths, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsMissing()
    {
        var result = _store.Load();
        Assert.Equal(FileLoadStatus.Missing, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SaveAndLoad_ApiMetadata_Roundtrip()
    {
        var metadata = new InstallationMetadata
        {
            ModeSlug = "full-ukrainian",
            PublicId = "01KZFM8YZBEBYF9JYSACTR8XW9",
            Version = 2,
            GamePatch = 396,
            Sha256 = "3b2fce8035666a5251878ce434f741dbdcd62574686ae42c87663097546c3ecf",
            InstalledAt = new DateTimeOffset(2026, 8, 13, 15, 30, 0, TimeSpan.FromHours(3)),
            Source = "api"
        };

        await _store.SaveAsync(metadata);
        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        var loaded = result.Value!;
        Assert.Equal("full-ukrainian", loaded.ModeSlug);
        Assert.Equal("01KZFM8YZBEBYF9JYSACTR8XW9", loaded.PublicId);
        Assert.Equal(2, loaded.Version);
        Assert.Equal(396, loaded.GamePatch);
        Assert.Equal("3b2fce8035666a5251878ce434f741dbdcd62574686ae42c87663097546c3ecf", loaded.Sha256);
        Assert.Equal("api", loaded.Source);
    }

    [Fact]
    public async Task SaveAndLoad_OfficialMetadata_Roundtrip()
    {
        var metadata = new InstallationMetadata
        {
            ModeSlug = null,
            PublicId = null,
            Version = null,
            GamePatch = 396,
            Sha256 = null,
            InstalledAt = new DateTimeOffset(2026, 8, 13, 16, 0, 0, TimeSpan.FromHours(3)),
            Source = "official"
        };

        await _store.SaveAsync(metadata);
        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        var loaded = result.Value!;
        Assert.Null(loaded.ModeSlug);
        Assert.Null(loaded.PublicId);
        Assert.Null(loaded.Version);
        Assert.Equal(396, loaded.GamePatch);
        Assert.Null(loaded.Sha256);
        Assert.Equal("official", loaded.Source);
    }

    [Fact]
    public async Task SaveAndLoad_TimestampRoundtrip()
    {
        var ts = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(3));
        var metadata = new InstallationMetadata
        {
            GamePatch = 396,
            InstalledAt = ts,
            Source = "api"
        };

        await _store.SaveAsync(metadata);
        var result = _store.Load();

        Assert.Equal(ts, result.Value!.InstalledAt);
    }

    [Fact]
    public async Task MalformedExistingFile_ReturnsInvalid()
    {
        await File.WriteAllTextAsync(_paths.InstallationFile, "{invalid json");

        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task MalformedFile_DoesNotModifyFile()
    {
        var originalContent = "{invalid json";
        await File.WriteAllTextAsync(_paths.InstallationFile, originalContent);

        _store.Load();

        var content = await File.ReadAllTextAsync(_paths.InstallationFile);
        Assert.Equal(originalContent, content);
    }

    [Fact]
    public async Task Clear_RemovesFile()
    {
        var metadata = new InstallationMetadata { GamePatch = 396, Source = "api" };
        await _store.SaveAsync(metadata);

        await _store.ClearAsync();

        var result = _store.Load();
        Assert.Equal(FileLoadStatus.Missing, result.Status);
    }

    [Fact]
    public async Task Clear_NonexistentFile_DoesNotThrow()
    {
        await _store.ClearAsync();
    }

    [Fact]
    public async Task Save_NoTempFileRemainsAfterSave()
    {
        var metadata = new InstallationMetadata { GamePatch = 396, Source = "api" };
        await _store.SaveAsync(metadata);

        var tempFile = _paths.InstallationFile + ".tmp";
        Assert.False(File.Exists(tempFile));
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

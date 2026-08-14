using System.Text;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Tests.Services;

public class LocalizationStateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly NullLogger _logger = new();

    public LocalizationStateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateGameFile(byte[] content)
    {
        var dir = Path.Combine(_tempDir, "game", "ads");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "languagedata_en.loc");
        File.WriteAllBytes(path, content);
        return path;
    }

    private async Task SaveApiMetadataAsync(string publicId, string sha256, int version = 1, int gamePatch = 100)
    {
        var metadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            PublicId = publicId,
            Version = version,
            GamePatch = gamePatch,
            Sha256 = sha256,
            InstalledAt = DateTimeOffset.UtcNow
        };
        var stateStore = new InstallationStateStore(_paths, _logger);
        await stateStore.SaveAsync(metadata);
    }

    private void SaveOfficialMetadata()
    {
        var metadata = new InstallationMetadata
        {
            Source = "official",
            InstalledAt = DateTimeOffset.UtcNow
        };
        var json = System.Text.Json.JsonSerializer.Serialize(metadata,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), json);
    }

    private void WriteMalformedMetadata(string content)
    {
        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), content);
    }

    private LocalizationStateService CreateService()
    {
        var stateStore = new InstallationStateStore(_paths, _logger);
        return new LocalizationStateService(stateStore, _logger);
    }

    private CurrentRelease CreateCurrent(string publicId = "01ABCDEF1234567890ABCDEF", int version = 2, int patch = 101)
    {
        return new CurrentRelease
        {
            PublicId = publicId,
            Version = version,
            Patch = patch,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = true,
            PublishedAt = "2026-01-01T00:00:00Z"
        };
    }

    // --- NotInstalled ---

    [Fact]
    public async Task MissingMetadata_ReturnsNotInstalled()
    {
        var service = CreateService();
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("content"));

        var state = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.NotInstalled, state);
    }

    [Fact]
    public async Task ValidOfficialMetadata_ReturnsNotInstalled()
    {
        var service = CreateService();
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("content"));
        SaveOfficialMetadata();

        var state = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.NotInstalled, state);
    }

    // --- InstalledVersionUnknown ---

    [Fact]
    public async Task MalformedJson_ReturnsInstalledVersionUnknown()
    {
        var service = CreateService();
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("content"));
        WriteMalformedMetadata("{not valid json!!!");

        var state = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.InstalledVersionUnknown, state);
    }

    [Fact]
    public async Task SemanticallyInvalidMetadata_ReturnsInstalledVersionUnknown()
    {
        var service = CreateService();
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("content"));
        // Missing required fields (no PublicId, no Sha256)
        var metadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            InstalledAt = DateTimeOffset.UtcNow
        };
        var json = System.Text.Json.JsonSerializer.Serialize(metadata,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), json);

        var state = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.InstalledVersionUnknown, state);
    }

    // --- Corrupted ---

    [Fact]
    public async Task ValidApiMetadata_GameFileMissing_ReturnsCorrupted()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var service = CreateService();

        // Game file doesn't exist at path
        var missingPath = Path.Combine(_tempDir, "nonexistent", "ads", "languagedata_en.loc");

        var state = await service.ResolveAsync(CreateCurrent(), missingPath);

        Assert.Equal(LocalizationState.Corrupted, state);
    }

    [Fact]
    public async Task ValidApiMetadata_HashMismatch_ReturnsCorrupted()
    {
        var content = Encoding.UTF8.GetBytes("original content");
        var wrongSha = HashHelper.ComputeSha256(Encoding.UTF8.GetBytes("different content"));
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", wrongSha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var state = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.Corrupted, state);
    }

    // --- WaitingForRelease ---

    [Fact]
    public async Task ValidApiMetadata_MatchingHash_CurrentNull_ReturnsWaitingForRelease()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var state = await service.ResolveAsync(null, gamePath);

        Assert.Equal(LocalizationState.WaitingForRelease, state);
    }

    // --- UpToDate ---

    [Fact]
    public async Task ValidApiMetadata_MatchingHash_SamePublicId_ReturnsUpToDate()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        var publicId = "01ABCDEF1234567890ABCDEF";
        await SaveApiMetadataAsync(publicId, sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var state = await service.ResolveAsync(CreateCurrent(publicId), gamePath);

        Assert.Equal(LocalizationState.UpToDate, state);
    }

    [Fact]
    public async Task SamePublicId_DifferentVersionAndPatch_ReturnsUpToDate()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        var publicId = "01ABCDEF1234567890ABCDEF";
        await SaveApiMetadataAsync(publicId, sha, version: 1, gamePatch: 100);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        // Current has different version and patch but same public_id
        var current = CreateCurrent(publicId, version: 99, patch: 999);

        var state = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpToDate, state);
    }

    // --- UpdateAvailable ---

    [Fact]
    public async Task ValidApiMetadata_MatchingHash_DifferentPublicId_ReturnsUpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var state = await service.ResolveAsync(CreateCurrent("01ZZZZZZZZZZZZZZZZZZZZZZ"), gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, state);
    }

    [Fact]
    public async Task DifferentPublicId_SameVersionAndPatch_ReturnsUpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha, version: 5, gamePatch: 200);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        // Current has same version and patch but different public_id
        var current = CreateCurrent("01ZZZZZZZZZZZZZZZZZZZZZZ", version: 5, patch: 200);

        var state = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, state);
    }

    // --- Cancellation ---

    [Fact]
    public async Task CancellationDuringHash_PropagatesOCE()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ResolveAsync(CreateCurrent(), gamePath, cts.Token));
    }

    // --- PublicId identity (not version/patch) ---

    [Fact]
    public async Task PublicIdIsPrimaryIdentity_NotVersionOrPatch()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        var publicId = "01ABCDEF1234567890ABCDEF";
        await SaveApiMetadataAsync(publicId, sha, version: 1, gamePatch: 100);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        // Current with same public_id but completely different version/patch
        var current = new CurrentRelease
        {
            PublicId = publicId,
            Version = 999,
            Patch = 9999,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = true,
            PublishedAt = "2026-01-01T00:00:00Z"
        };

        var state = await service.ResolveAsync(current, gamePath);

        // Must be UpToDate because public_id matches, regardless of version/patch
        Assert.Equal(LocalizationState.UpToDate, state);
    }

    [Fact]
    public async Task DifferentPublicId_SameVersion_Patch_ReturnsUpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01OLDID00000000000000000", sha, version: 5, gamePatch: 200);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        // Current has different public_id but same version/patch
        var current = new CurrentRelease
        {
            PublicId = "01NEWID00000000000000000",
            Version = 5,
            Patch = 200,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = true,
            PublishedAt = "2026-01-01T00:00:00Z"
        };

        var state = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, state);
    }

    // --- Corrupted via RollbackFailed scenario ---

    [Fact]
    public async Task ValidApiMetadata_FileExistsButContentCorrupted_ReturnsCorrupted()
    {
        var content = Encoding.UTF8.GetBytes("original content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        // Write different content to game file
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("corrupted content"));
        var service = CreateService();

        var state = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.Corrupted, state);
    }

    // --- Current with empty PublicId ---

    [Fact]
    public async Task ValidApiMetadata_MatchingHash_CurrentWithEmptyPublicId_ReturnsWaitingForRelease()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var current = new CurrentRelease
        {
            PublicId = "",
            Version = 2,
            Patch = 101,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = true,
            PublishedAt = "2026-01-01T00:00:00Z"
        };

        var state = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.WaitingForRelease, state);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

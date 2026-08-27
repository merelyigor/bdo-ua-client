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

    private string GameRoot => Path.Combine(_tempDir, "game");

    private void WriteAdsFiles(string content) => File.WriteAllText(Path.Combine(GameRoot, "ads_files"), content);

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

        var result = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.NotInstalled, result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidOfficialMetadata_ReturnsNotInstalled()
    {
        var service = CreateService();
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("content"));
        SaveOfficialMetadata();

        var result = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.NotInstalled, result.State);
        Assert.Null(result.Error);
    }

    // --- InstalledVersionUnknown ---

    [Fact]
    public async Task MalformedJson_ReturnsInstalledVersionUnknown()
    {
        var service = CreateService();
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("content"));
        WriteMalformedMetadata("{not valid json!!!");

        var result = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.InstalledVersionUnknown, result.State);
        Assert.Null(result.Error);
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

        var result = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.InstalledVersionUnknown, result.State);
        Assert.Null(result.Error);
    }

    // --- Corrupted ---

    [Fact]
    public async Task ValidApiMetadata_GameFileMissing_ReturnsCorrupted()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var service = CreateService();

        var missingPath = Path.Combine(_tempDir, "nonexistent", "ads", "languagedata_en.loc");

        var result = await service.ResolveAsync(CreateCurrent(), missingPath);

        Assert.Equal(LocalizationState.Corrupted, result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidApiMetadata_HashMismatch_ManagedFileChanged()
    {
        var content = Encoding.UTF8.GetBytes("original content");
        var wrongSha = HashHelper.ComputeSha256(Encoding.UTF8.GetBytes("different content"));
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", wrongSha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var result = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Equal(LocalizationPatchTransition.ManagedFileChanged, result.PatchTransition);
    }

    [Fact]
    public async Task ValidApiMetadata_FileExistsButContentDifferent_ManagedFileChanged()
    {
        var content = Encoding.UTF8.GetBytes("original content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("different content"));
        var service = CreateService();

        var result = await service.ResolveAsync(CreateCurrent(), gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Equal(LocalizationPatchTransition.ManagedFileChanged, result.PatchTransition);
    }

    [Fact]
    public async Task GamePatchTransition_HashMismatch_IsNotCorrupted()
    {
        var installedContent = Encoding.UTF8.GetBytes("installed patch 397");
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", HashHelper.ComputeSha256(installedContent), gamePatch: 397);
        var gamePath = CreateGameFile(Encoding.UTF8.GetBytes("game replaced file"));
        WriteAdsFiles("languagedata_en.loc\t398\n");

        var result = await CreateService().ResolveAsync(CreateCurrent(), gamePath, gameRoot: GameRoot);

        Assert.NotEqual(LocalizationState.Corrupted, result.State);
        Assert.Equal(LocalizationPatchTransition.GameFileReplacedAfterPatch, result.PatchTransition);
        Assert.Contains("397", result.Error);
        Assert.Contains("398", result.Error);
    }

    [Fact]
    public async Task GamePatchTransition_HashMatch_IsOutdatedPatchState()
    {
        var content = Encoding.UTF8.GetBytes("installed patch 397");
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", HashHelper.ComputeSha256(content), gamePatch: 397);
        var gamePath = CreateGameFile(content);
        WriteAdsFiles("languagedata_en.loc    398\n");

        var result = await CreateService().ResolveAsync(CreateCurrent(), gamePath, gameRoot: GameRoot);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Equal(LocalizationPatchTransition.ExistingLocalizationOutdated, result.PatchTransition);
        Assert.Contains("397", result.Error);
        Assert.Contains("398", result.Error);
    }

    [Fact]
    public async Task MatchingGamePatch_HashMismatch_ManagedFileChanged_UpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("installed patch 398");
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", HashHelper.ComputeSha256(Encoding.UTF8.GetBytes("different")), gamePatch: 398);
        var gamePath = CreateGameFile(content);
        WriteAdsFiles("languagedata_en.loc 398\n");

        var result = await CreateService().ResolveAsync(CreateCurrent(), gamePath, gameRoot: GameRoot);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Equal(LocalizationPatchTransition.ManagedFileChanged, result.PatchTransition);
        Assert.DoesNotContain("пошкоджено", result.Error ?? "");
    }

    [Fact]
    public async Task NoAdsFilesPatch_HashMismatch_ManagedFileChanged_UpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("installed content");
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", HashHelper.ComputeSha256(Encoding.UTF8.GetBytes("different")), gamePatch: 398);
        var gamePath = CreateGameFile(content);

        var result = await CreateService().ResolveAsync(CreateCurrent(), gamePath, gameRoot: GameRoot);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Equal(LocalizationPatchTransition.ManagedFileChanged, result.PatchTransition);
        Assert.DoesNotContain("пошкоджено", result.Error ?? "");
    }

    [Fact]
    public async Task SamePublicId_HashMismatch_ManagedFileChanged_UpdateAvailable()
    {
        var publicId = "01ABCDEF1234567890ABCDEF";
        var content = Encoding.UTF8.GetBytes("installed content");
        await SaveApiMetadataAsync(publicId, HashHelper.ComputeSha256(Encoding.UTF8.GetBytes("different")), gamePatch: 398);
        var gamePath = CreateGameFile(content);
        WriteAdsFiles("languagedata_en.loc 398\n");

        var result = await CreateService().ResolveAsync(CreateCurrent(publicId), gamePath, gameRoot: GameRoot);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Equal(LocalizationPatchTransition.ManagedFileChanged, result.PatchTransition);
    }

    [Fact]
    public async Task HashMismatch_CurrentNull_ManagedFileChanged_WaitingForRelease()
    {
        var content = Encoding.UTF8.GetBytes("installed content");
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", HashHelper.ComputeSha256(Encoding.UTF8.GetBytes("different")), gamePatch: 398);
        var gamePath = CreateGameFile(content);
        WriteAdsFiles("languagedata_en.loc 398\n");

        var result = await CreateService().ResolveAsync(null, gamePath, gameRoot: GameRoot);

        Assert.Equal(LocalizationState.WaitingForRelease, result.State);
        Assert.Equal(LocalizationPatchTransition.ManagedFileChanged, result.PatchTransition);
        Assert.Contains("більше не активна", result.Error ?? "");
    }

    [Fact]
    public async Task GamePatchTransition_CurrentNull_IsWaitingWithContext()
    {
        var content = Encoding.UTF8.GetBytes("installed patch 397");
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", HashHelper.ComputeSha256(content), gamePatch: 397);
        var gamePath = CreateGameFile(content);
        WriteAdsFiles("languagedata_en.loc 398\n");

        var result = await CreateService().ResolveAsync(null, gamePath, gameRoot: GameRoot);

        Assert.Equal(LocalizationState.WaitingForRelease, result.State);
        Assert.Contains("патча 398", result.Error);
        Assert.DoesNotContain("Current release is not available", result.Error);
    }

    // --- WaitingForRelease ---

    [Fact]
    public async Task ValidApiMetadata_MatchingHash_CurrentNull_ReturnsWaitingForRelease_NoError()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var result = await service.ResolveAsync(null, gamePath);

        Assert.Equal(LocalizationState.WaitingForRelease, result.State);
        Assert.Null(result.Error);
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

        var result = await service.ResolveAsync(CreateCurrent(publicId), gamePath);

        Assert.Equal(LocalizationState.UpToDate, result.State);
        Assert.Null(result.Error);
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

        var current = CreateCurrent(publicId, version: 99, patch: 999);

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpToDate, result.State);
        Assert.Null(result.Error);
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

        var result = await service.ResolveAsync(CreateCurrent("01ZZZZZZZZZZZZZZZZZZZZZZ"), gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DifferentPublicId_SameVersionAndPatch_ReturnsUpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha, version: 5, gamePatch: 200);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var current = CreateCurrent("01ZZZZZZZZZZZZZZZZZZZZZZ", version: 5, patch: 200);

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Null(result.Error);
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
            () => service.ResolveAsync(CreateCurrent(), gamePath, cancellationToken: cts.Token));
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

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpToDate, result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DifferentPublicId_SameVersion_Patch_ReturnsUpdateAvailable()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01OLDID00000000000000000", sha, version: 5, gamePatch: 200);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

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

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.UpdateAvailable, result.State);
        Assert.Null(result.Error);
    }

    // --- Invalid current metadata: null PublicId ---

    [Fact]
    public async Task CurrentPublicIdNull_ReturnsWaitingForRelease_WithError()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var current = new CurrentRelease
        {
            PublicId = null,
            Version = 2,
            Patch = 101,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = true,
            PublishedAt = "2026-01-01T00:00:00Z"
        };

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.WaitingForRelease, result.State);
        Assert.NotNull(result.Error);
        Assert.Contains("Актуальний українізатор", result.Error);
    }

    // --- Invalid current metadata: empty PublicId ---

    [Fact]
    public async Task CurrentPublicIdEmpty_ReturnsWaitingForRelease_WithError()
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

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.WaitingForRelease, result.State);
        Assert.NotNull(result.Error);
        Assert.Contains("Актуальний українізатор", result.Error);
    }

    // --- Invalid current metadata: whitespace PublicId ---

    [Fact]
    public async Task CurrentPublicIdWhitespace_ReturnsWaitingForRelease_WithError()
    {
        var content = Encoding.UTF8.GetBytes("content");
        var sha = HashHelper.ComputeSha256(content);
        await SaveApiMetadataAsync("01ABCDEF1234567890ABCDEF", sha);
        var gamePath = CreateGameFile(content);
        var service = CreateService();

        var current = new CurrentRelease
        {
            PublicId = "   ",
            Version = 2,
            Patch = 101,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = true,
            PublishedAt = "2026-01-01T00:00:00Z"
        };

        var result = await service.ResolveAsync(current, gamePath);

        Assert.Equal(LocalizationState.WaitingForRelease, result.State);
        Assert.NotNull(result.Error);
        Assert.Contains("Актуальний українізатор", result.Error);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

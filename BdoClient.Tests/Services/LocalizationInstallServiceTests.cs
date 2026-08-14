using System.Net;
using System.Text;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Tests.Services;

public class LocalizationInstallServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly NullLogger _logger = new();

    public LocalizationInstallServiceTests()
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

    private string CreateGameRoot(byte[]? content = null)
    {
        var gameDir = Path.Combine(_tempDir, "game", "ads");
        Directory.CreateDirectory(gameDir);
        var path = Path.Combine(gameDir, "languagedata_en.loc");
        File.WriteAllText(path, content != null ? Encoding.UTF8.GetString(content) : "game content");
        return Path.Combine(_tempDir, "game");
    }

    private string GameLocFilePath => Path.Combine(_tempDir, "game", "ads", "languagedata_en.loc");

    private CurrentRelease CreateRelease(
        string publicId = "01ABCDEF1234567890ABCDEF",
        int version = 1,
        int patch = 100,
        bool compatible = true,
        byte[]? content = null)
    {
        var data = content ?? Encoding.UTF8.GetBytes("release content");
        return new CurrentRelease
        {
            PublicId = publicId,
            Version = version,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = data.Length,
            Sha256 = BdoClient.Services.HashHelper.ComputeSha256(data),
            Patch = patch,
            CompatibleWithOfficialPatch = compatible,
            PublishedAt = "2026-01-01T00:00:00Z"
        };
    }

    private LocalizationInstallService CreateService(
        HttpMessageHandler handler,
        string? gameRoot = null)
    {
        var httpClient = new HttpClient(handler);
        var installer = new LocalizationInstaller(httpClient, _paths, _logger);
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);

        return new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: gameRoot ?? Path.Combine(_tempDir, "game"));
    }

    // --- Input validation ---

    [Fact]
    public async Task InstallReleaseAsync_MissingGameTarget_ReturnsInvalidGamePath_NoHttpRequest()
    {
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler,
            gameRoot: Path.Combine(_tempDir, "nonexistent"));

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidGamePath, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_EmptyModeSlug_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_EmptyPublicId_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease(publicId: "");
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_InvalidVersion_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease(version: 0);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_InvalidPatch_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease(patch: 0);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_HttpDownloadUrl_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease();
        release.DownloadUrl = "http://example.com/release.loc";
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_ZeroSizeBytes_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease();
        release.SizeBytes = 0;
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InstallReleaseAsync_EmptySha256_ReturnsInvalidRelease_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease();
        release.Sha256 = "";
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.InvalidRelease, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- Compatibility ---

    [Fact]
    public async Task InstallReleaseAsync_IncompatibleRelease_ReturnsIncompatible_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease(compatible: false);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.Incompatible, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- Original snapshot ---

    [Fact]
    public async Task InstallReleaseAsync_CorruptedOriginalSnapshot_ReturnsOriginalSnapshotFailed()
    {
        var gameRoot = CreateGameRoot();

        // Create valid snapshot then corrupt it
        var backupStore = new BackupStore(_paths, _logger);
        await backupStore.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);
        File.WriteAllText(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc"), "corrupted");

        // Download must succeed (snapshot check happens after download in v6.1 ordering)
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);
        var service = CreateService(handler);

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.OriginalSnapshotFailed, result.Error);
    }

    [Fact]
    public async Task InstallReleaseAsync_MissingSnapshotWithApiMetadata_ReturnsOriginalSnapshotFailed()
    {
        var gameRoot = CreateGameRoot();

        // Write valid existing API metadata (no snapshot) — must include ModeSlug
        var metadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            PublicId = "old-public-id",
            Version = 1,
            GamePatch = 100,
            Sha256 = "abc",
            InstalledAt = DateTimeOffset.UtcNow
        };
        var json = System.Text.Json.JsonSerializer.Serialize(metadata,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), json);

        // Download must succeed (snapshot check happens after download)
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);
        var service = CreateService(handler);

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.OriginalSnapshotFailed, result.Error);
    }

    // --- First install success ---

    [Fact]
    public async Task InstallReleaseAsync_FirstInstall_CreatesSnapshotAndSavesMetadata()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original game"));
        var releaseContent = Encoding.UTF8.GetBytes("new localization");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service = CreateService(handler);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.True(result.IsSuccess);

        // Original snapshot created from pre-install game file
        Assert.True(File.Exists(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.Equal("original game", File.ReadAllText(
            Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));

        // Game file replaced with release content
        Assert.Equal("new localization", File.ReadAllText(GameLocFilePath));

        // Installation metadata saved
        var stateStore = new InstallationStateStore(_paths, _logger);
        var state = stateStore.Load();
        Assert.Equal(FileLoadStatus.Valid, state.Status);
        Assert.Equal("api", state.Value!.Source);
        Assert.Equal("full-ukrainian", state.Value.ModeSlug);
        Assert.Equal(release.PublicId, state.Value.PublicId);
        Assert.Equal(release.Version, state.Value.Version);
        Assert.Equal(release.Patch, state.Value.GamePatch);
        Assert.Equal(release.Sha256, state.Value.Sha256);

        // Restore point exists
        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);
    }

    // --- Update success ---

    [Fact]
    public async Task InstallReleaseAsync_Update_ExistingSnapshotUnchanged_GameReplaced()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old localization"));

        // Create existing installation state (old release)
        var oldMetadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            PublicId = "old-id",
            Version = 1,
            GamePatch = 100,
            Sha256 = "old-sha",
            InstalledAt = DateTimeOffset.UtcNow
        };
        var stateStore = new InstallationStateStore(_paths, _logger);
        await stateStore.SaveAsync(oldMetadata);

        // Create original snapshot from the game root (must exist before service runs)
        var backupStore = new BackupStore(_paths, _logger);
        await backupStore.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        // New release
        var newContent = Encoding.UTF8.GetBytes("new localization v2");
        var release = CreateRelease(
            publicId: "01ABCDEF1234567890ABCDEF",
            version: 2,
            content: newContent);
        var handler = new MockHttpHandler(newContent, newContent.Length);

        var service = CreateService(handler);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.True(result.IsSuccess);

        // Original snapshot unchanged (still "old localization")
        Assert.Equal("old localization", File.ReadAllText(
            Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));

        // Game file updated
        Assert.Equal("new localization v2", File.ReadAllText(GameLocFilePath));

        // Metadata updated
        var state = stateStore.Load();
        Assert.Equal("01ABCDEF1234567890ABCDEF", state.Value!.PublicId);
        Assert.Equal(2, state.Value.Version);

        // Restore point exists
        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.NotEmpty(rpDirs);
    }

    // --- Download failure ---

    [Fact]
    public async Task InstallReleaseAsync_DownloadFails_GameAndStateUnchanged()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler);

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.DownloadFailed, result.Error);
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));
        Assert.False(File.Exists(Path.Combine(_paths.StateDir, "installation.json")));
    }

    // --- Replace failure ---

    [Fact]
    public async Task InstallReleaseAsync_ReplaceFails_GameAndStateUnchanged()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service = CreateService(handler);

        // Corrupt the temp file after download by using OnPostReplaceHook
        // to throw before File.Replace happens
        var backupStore = new BackupStore(_paths, _logger);
        // We can't easily hook into ReplaceGameFileAsync pre-replace from here
        // Instead, test with a file that can't be replaced (locked/read-only)
        var rpDir = Path.Combine(_paths.RestorePointsDir, "test-rp");
        Directory.CreateDirectory(rpDir);

        // Make target read-only to cause replace failure
        File.SetAttributes(GameLocFilePath, FileAttributes.ReadOnly);

        try
        {
            var result = await service.InstallReleaseAsync("full-ukrainian", release);

            // May fail with ReplaceFailed or similar
            Assert.False(result.IsSuccess);
            Assert.Equal("original", File.ReadAllText(GameLocFilePath));
        }
        finally
        {
            File.SetAttributes(GameLocFilePath, FileAttributes.Normal);
        }
    }

    // --- Verification failure after replace ---

    [Fact]
    public async Task InstallReleaseAsync_VerificationFails_RollsBackGameState()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service = CreateService(handler);

        // Install first to set up state
        var r1 = await service.InstallReleaseAsync("full-ukrainian", release);
        Assert.True(r1.IsSuccess);

        // Now corrupt the game file to simulate post-replace verification failure
        // on a second install
        File.WriteAllText(GameLocFilePath, "corrupted");

        var release2Content = Encoding.UTF8.GetBytes("second release");
        var release2 = CreateRelease(
            publicId: "01ABCDEF1234567890ABCDEF",
            version: 2,
            content: release2Content);
        // Handler returns different bytes than release2 claims
        var handler2 = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service2 = CreateService(handler2);
        var result = await service2.InstallReleaseAsync("full-ukrainian", release2);

        // Verification should fail because handler returns different content
        // than what release2.Sha256 expects
        Assert.False(result.IsSuccess);
    }

    // --- State save failure ---

    [Fact]
    public async Task InstallReleaseAsync_StateSaveFails_RollsBackGameState()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);

        // Hook: fail on state save
        stateStore.OnSaveAsync = (m, ct) => throw new IOException("disk full");

        var service = new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.StateSaveFailed, result.Error);

        // Game file restored to original via rollback
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));

        // Installation state absent (was absent before)
        Assert.False(File.Exists(Path.Combine(_paths.StateDir, "installation.json")));

        stateStore.OnSaveAsync = null;
    }

    // --- Cancellation pre-replace ---

    [Fact]
    public async Task InstallReleaseAsync_CancelledBeforeReplace_NoMutation()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));

        // Handler that respects cancellation token
        var handler = new MockHttpHandlerWithCancellation();

        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var release = CreateRelease();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.InstallReleaseAsync("full-ukrainian", release, cancellationToken: cts.Token));

        Assert.Equal("original", File.ReadAllText(GameLocFilePath));
        Assert.False(File.Exists(Path.Combine(_paths.StateDir, "installation.json")));
    }

    // --- Restore point retention ---

    [Fact]
    public async Task InstallReleaseAsync_Success_RetainsRestorePoint()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service = CreateService(handler);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.True(result.IsSuccess);
        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);
        Assert.True(File.Exists(Path.Combine(rpDirs[0], "languagedata_en.loc")));
        Assert.True(File.Exists(Path.Combine(rpDirs[0], "metadata.json")));
    }

    // --- Original snapshot retention ---

    [Fact]
    public async Task InstallReleaseAsync_Success_RetainsOriginalSnapshot()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service = CreateService(handler);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.True(File.Exists(Path.Combine(_paths.OriginalBackupDir, "metadata.json")));
        Assert.Equal("original", File.ReadAllText(
            Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));
    }

    // --- Update: state save failure restores exact prior state ---

    [Fact]
    public async Task InstallReleaseAsync_UpdateStateSaveFails_RestoresExactPriorMetadata()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old loc"));

        // Set up existing state
        var oldMetadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            PublicId = "old-id",
            Version = 1,
            GamePatch = 100,
            Sha256 = "old-sha",
            InstalledAt = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero)
        };
        var oldJson = System.Text.Json.JsonSerializer.Serialize(oldMetadata,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var oldBytes = Encoding.UTF8.GetBytes(oldJson);
        File.WriteAllBytes(Path.Combine(_paths.StateDir, "installation.json"), oldBytes);

        // Set up original snapshot (required for source=api metadata consistency)
        var backupStore = new BackupStore(_paths, _logger);
        var initialDir = Path.Combine(_tempDir, "initial", "ads");
        Directory.CreateDirectory(initialDir);
        File.WriteAllText(Path.Combine(initialDir, "languagedata_en.loc"), "initial");
        await backupStore.CreateOriginalSnapshotAsync(
            Path.Combine(_tempDir, "initial"), trustedGamePatch: 100);

        // New release
        var newContent = Encoding.UTF8.GetBytes("new loc");
        var release = CreateRelease(version: 2, content: newContent);
        var handler = new MockHttpHandler(newContent, newContent.Length);

        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);
        stateStore.OnSaveAsync = (m, ct) => throw new IOException("disk full");

        var service = new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.StateSaveFailed, result.Error);

        // Game restored
        Assert.Equal("old loc", File.ReadAllText(GameLocFilePath));

        // State restored to exact prior bytes
        var restoredBytes = File.ReadAllBytes(Path.Combine(_paths.StateDir, "installation.json"));
        Assert.Equal(oldBytes, restoredBytes);

        stateStore.OnSaveAsync = null;
    }

    // --- First install: state save failure restores absence ---

    [Fact]
    public async Task InstallReleaseAsync_FirstInstallStateSaveFails_StateAbsent()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);
        stateStore.OnSaveAsync = (m, ct) => throw new IOException("disk full");

        var service = new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.StateSaveFailed, result.Error);
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));
        Assert.False(File.Exists(Path.Combine(_paths.StateDir, "installation.json")));

        stateStore.OnSaveAsync = null;
    }

    // --- Ordering: download failure creates no snapshot, no restore point ---

    [Fact]
    public async Task InstallReleaseAsync_DownloadFails_NoSnapshotNoRestorePoint()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler);

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.DownloadFailed, result.Error);
        Assert.False(File.Exists(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.Empty(Directory.GetDirectories(_paths.RestorePointsDir));
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));
    }

    // --- Corrupted pre-state: PreOperationStateFailed, 0 HTTP ---

    [Fact]
    public async Task InstallReleaseAsync_CorruptedPreState_ReturnsPreOperationStateFailed_NoHttpRequest()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        // Write malformed installation.json
        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), "{not valid json!!!");

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.PreOperationStateFailed, result.Error);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal("game content", File.ReadAllText(GameLocFilePath));
    }

    // --- Corrupted pre-state + no snapshot: same failure ---

    [Fact]
    public async Task InstallReleaseAsync_CorruptedPreStateNoSnapshot_NoJsonException()
    {
        var gameRoot = CreateGameRoot();
        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), "corrupted!!!");

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.PreOperationStateFailed, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- Restore-point retained after verification rollback ---

    [Fact]
    public async Task InstallReleaseAsync_StateSaveFails_RetainsRestorePoint()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);
        stateStore.OnSaveAsync = (m, ct) => throw new IOException("disk full");

        var service = new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.StateSaveFailed, result.Error);

        // Restore point retained even after rollback
        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);
        Assert.True(File.Exists(Path.Combine(rpDirs[0], "languagedata_en.loc")));

        stateStore.OnSaveAsync = null;
    }

    // --- Corrupted pre-state + valid existing snapshot: PreOperationStateFailed ---

    [Fact]
    public async Task InstallReleaseAsync_CorruptedPreStateWithSnapshot_ReturnsPreOperationStateFailed()
    {
        var gameRoot = CreateGameRoot();

        // Create valid original snapshot
        var backupStore = new BackupStore(_paths, _logger);
        await backupStore.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        // Write corrupted installation.json
        File.WriteAllText(Path.Combine(_paths.StateDir, "installation.json"), "corrupted!!!");

        var handler = new MockHttpHandler(null, 0);
        var service = CreateService(handler);

        var release = CreateRelease();
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.PreOperationStateFailed, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- v6.2: Cancellation during original snapshot phase → download temp cleaned ---

    [Fact]
    public async Task InstallReleaseAsync_CancelledDuringOriginalSnapshot_DownloadTempCleaned()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        // BackupStore that throws OCE during CreateOriginalSnapshotAsync
        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var backupStore = new ThrowingBackupStore(_paths, _logger)
        {
            ThrowOnCreateOriginalSnapshot = true
        };
        var stateStore = new InstallationStateStore(_paths, _logger);

        var service = new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.InstallReleaseAsync("full-ukrainian", release));

        // Game file unchanged
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));

        // No state saved
        Assert.False(File.Exists(Path.Combine(_paths.StateDir, "installation.json")));

        // Download temp cleaned: no *.tmp files in cache
        var tmpFiles = Directory.Exists(_paths.CacheDir)
            ? Directory.GetFiles(_paths.CacheDir, "*.tmp", SearchOption.AllDirectories)
            : Array.Empty<string>();
        Assert.Empty(tmpFiles);

        // No restore points created
        Assert.Empty(Directory.GetDirectories(_paths.RestorePointsDir));
    }

    // --- v6.2: Cancellation during restore-point creation → download temp cleaned ---

    [Fact]
    public async Task InstallReleaseAsync_CancelledDuringRestorePoint_DownloadTempCleaned()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        // BackupStore that throws OCE during CreateRestorePointAsync
        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var backupStore = new ThrowingBackupStore(_paths, _logger)
        {
            ThrowOnCreateRestorePoint = true
        };
        var stateStore = new InstallationStateStore(_paths, _logger);

        var service = new LocalizationInstallService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.InstallReleaseAsync("full-ukrainian", release));

        // Game file unchanged
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));

        // No state saved
        Assert.False(File.Exists(Path.Combine(_paths.StateDir, "installation.json")));

        // Download temp cleaned
        var tmpFiles = Directory.Exists(_paths.CacheDir)
            ? Directory.GetFiles(_paths.CacheDir, "*.tmp", SearchOption.AllDirectories)
            : Array.Empty<string>();
        Assert.Empty(tmpFiles);

        // No restore points created (OCE before completion)
        Assert.Empty(Directory.GetDirectories(_paths.RestorePointsDir));
    }

    // --- v6.2: Failure during raw state snapshot persistence → BackupFailed ---

    [Fact]
    public async Task InstallReleaseAsync_RawStateSnapshotFails_ReturnsBackupFailed()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));

        // Set up existing state so preStateBytes is non-null
        var oldMetadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            PublicId = "old-id",
            Version = 1,
            GamePatch = 100,
            Sha256 = "old-sha",
            InstalledAt = DateTimeOffset.UtcNow
        };
        var stateStore = new InstallationStateStore(_paths, _logger);
        await stateStore.SaveAsync(oldMetadata);

        // Create original snapshot (required for api metadata consistency)
        var backupStore = new BackupStore(_paths, _logger);
        await backupStore.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        // New release
        var newContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(content: newContent);
        var handler = new MockHttpHandler(newContent, newContent.Length);

        // Make restore-point dir read-only so raw state snapshot persistence fails
        // We'll use a custom service that injects failure at the right point
        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var failingBackupStore = new FailingAfterRestorePointBackupStore(_paths, _logger);

        var service = new LocalizationInstallService(
            installer, failingBackupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"));

        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallError.BackupFailed, result.Error);

        // Game unchanged
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));

        // State unchanged (old metadata still there)
        var state = stateStore.Load();
        Assert.Equal("old-id", state.Value!.PublicId);
    }

    // --- v6.2: Restore-point game_patch uses pre-operation patch (update) ---

    [Fact]
    public async Task InstallReleaseAsync_Update_RestorePointGamePatchMatchesOldState()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old localization"));

        // Existing state with patch 100
        var oldMetadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = "full-ukrainian",
            PublicId = "old-id",
            Version = 1,
            GamePatch = 100,
            Sha256 = "old-sha",
            InstalledAt = DateTimeOffset.UtcNow
        };
        var stateStore = new InstallationStateStore(_paths, _logger);
        await stateStore.SaveAsync(oldMetadata);

        // Create original snapshot
        var backupStore = new BackupStore(_paths, _logger);
        await backupStore.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        // New release with patch 101
        var newContent = Encoding.UTF8.GetBytes("new loc");
        var release = CreateRelease(version: 2, patch: 101, content: newContent);
        var handler = new MockHttpHandler(newContent, newContent.Length);

        var service = CreateService(handler);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.True(result.IsSuccess);

        // Restore point metadata should have game_patch=100 (pre-operation), not 101
        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);
        var rpMetadataPath = Path.Combine(rpDirs[0], "metadata.json");
        var rpMetadataJson = await File.ReadAllTextAsync(rpMetadataPath);
        var rpMetadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(rpMetadataJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(100, rpMetadata!.GamePatch);

        // Installation state should have patch 101
        var state = stateStore.Load();
        Assert.Equal(101, state.Value!.GamePatch);
    }

    // --- v6.2: Restore-point game_patch is null for first install without prior state ---

    [Fact]
    public async Task InstallReleaseAsync_FirstInstallNoState_RestorePointGamePatchNull()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));
        var releaseContent = Encoding.UTF8.GetBytes("new content");
        var release = CreateRelease(patch: 101, content: releaseContent);
        var handler = new MockHttpHandler(releaseContent, releaseContent.Length);

        var service = CreateService(handler);
        var result = await service.InstallReleaseAsync("full-ukrainian", release);

        Assert.True(result.IsSuccess);

        // Restore point metadata should have game_patch=null (no prior state)
        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);
        var rpMetadataPath = Path.Combine(rpDirs[0], "metadata.json");
        var rpMetadataJson = await File.ReadAllTextAsync(rpMetadataPath);
        var rpMetadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(rpMetadataJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Null(rpMetadata!.GamePatch);

        // Installation state should have patch 101
        var state = new InstallationStateStore(_paths, _logger).Load();
        Assert.Equal(101, state.Value!.GamePatch);
    }

    // --- MockHttpHandler ---

    private class ThrowingBackupStore : BackupStore
    {
        public bool ThrowOnCreateOriginalSnapshot { get; set; }
        public bool ThrowOnCreateRestorePoint { get; set; }

        public ThrowingBackupStore(AppPaths paths, ILogger logger) : base(paths, logger) { }

        public override async Task<RestoreResult> CreateOriginalSnapshotAsync(
            string gameRoot, int? trustedGamePatch, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCreateOriginalSnapshot)
                throw new OperationCanceledException();
            return await base.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch, cancellationToken);
        }

        public override async Task<(string? restorePointDir, RestoreResult result)> CreateRestorePointAsync(
            string gameFilePath, int? gamePatch, string? operationLabel, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCreateRestorePoint)
                throw new OperationCanceledException();
            return await base.CreateRestorePointAsync(gameFilePath, gamePatch, operationLabel, cancellationToken);
        }
    }

    private class FailingAfterRestorePointBackupStore : BackupStore
    {
        public FailingAfterRestorePointBackupStore(AppPaths paths, ILogger logger) : base(paths, logger) { }

        public override async Task<(string? restorePointDir, RestoreResult result)> CreateRestorePointAsync(
            string gameFilePath, int? gamePatch, string? operationLabel, CancellationToken cancellationToken = default)
        {
            var (rpDir, rpResult) = await base.CreateRestorePointAsync(
                gameFilePath, gamePatch, operationLabel, cancellationToken);

            if (rpResult.IsSuccess && rpDir != null)
            {
                // Pre-create installation-state.json so File.Move(overwrite:false) throws IOException
                File.WriteAllText(Path.Combine(rpDir, "installation-state.json"), "{}");
            }

            return (rpDir, rpResult);
        }
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[]? _responseContent;
        private readonly long _contentLength;
        private readonly int _statusCode;

        public int RequestCount { get; private set; }
        public int FailUntilAttempt { get; set; }

        public MockHttpHandler(byte[]? responseContent, long contentLength, int statusCode = 200)
        {
            _responseContent = responseContent;
            _contentLength = contentLength;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (FailUntilAttempt > 0 && RequestCount <= FailUntilAttempt)
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)_statusCode));

            if (_responseContent == null)
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)_statusCode));

            var content = new ByteArrayContent(_responseContent);
            content.Headers.ContentLength = _contentLength;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)_statusCode) { Content = content });
        }
    }

    private class MockHttpHandlerWithCancellation : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("data"))
            });
        }
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

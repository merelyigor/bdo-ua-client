using System.Net;
using System.Text;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Tests.Services;

public class RestoreOriginalServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly NullLogger _logger = new();

    public RestoreOriginalServiceTests()
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

    private async Task CreateValidSnapshot(int? gamePatch, byte[]? content = null)
    {
        var backupStore = new BackupStore(_paths, _logger);
        var gameRoot = CreateGameRoot(content ?? Encoding.UTF8.GetBytes("snapshot content"));
        await backupStore.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: gamePatch);
    }

    private RestoreOriginalService CreateService(
        HttpMessageHandler handler, string? officialUrl = null, int? currentOfficialPatch = 100)
    {
        var httpClient = new HttpClient(handler);
        var installer = new LocalizationInstaller(httpClient, _paths, _logger);
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);

        return new RestoreOriginalService(
            installer, backupStore, stateStore, _logger,
            gameRoot: Path.Combine(_tempDir, "game"),
            officialSourceUrl: officialUrl ?? "https://example.com/loc.loc",
            currentOfficialPatch: currentOfficialPatch);
    }

    // --- Upfront validation: missing game target ---

    [Fact]
    public async Task RestoreOriginalAsync_MissingGameTarget_ReturnsInvalidGamePath_NoHttpRequest()
    {
        var handler = new MockHttpHandler(null, 0);
        var service = new RestoreOriginalService(
            new LocalizationInstaller(new HttpClient(handler), _paths, _logger),
            new BackupStore(_paths, _logger),
            new InstallationStateStore(_paths, _logger),
            _logger,
            gameRoot: Path.Combine(_tempDir, "nonexistent"),
            officialSourceUrl: "https://example.com/loc.loc",
            currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.InvalidGamePath, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // --- Official restore: success ---

    [Fact]
    public async Task RestoreOriginalAsync_OfficialSuccess_ReplacesFileAndSavesMetadata()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old content"));
        var officialContent = Encoding.UTF8.GetBytes("official restored content");
        var handler = new MockHttpHandler(officialContent, officialContent.Length);

        var service = CreateService(handler);

        var result = await service.RestoreOriginalAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("official restored content", File.ReadAllText(GameLocFilePath));

        var stateStore = new InstallationStateStore(_paths, _logger);
        var state = stateStore.Load();
        Assert.Equal("official", state.Value!.Source);
        Assert.Null(state.Value.PublicId);
        Assert.Null(state.Value.Sha256);
        Assert.Null(state.Value.ModeSlug);
    }

    [Fact]
    public async Task RestoreOriginalAsync_OfficialSuccess_CreatesRestorePoint()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old content"));
        var officialContent = Encoding.UTF8.GetBytes("new content");
        var handler = new MockHttpHandler(officialContent, officialContent.Length);

        var service = CreateService(handler);

        await service.RestoreOriginalAsync();

        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);

        var rpFile = Path.Combine(rpDirs[0], "languagedata_en.loc");
        var rpMetadata = Path.Combine(rpDirs[0], "metadata.json");
        Assert.True(File.Exists(rpFile));
        Assert.True(File.Exists(rpMetadata));
    }

    [Fact]
    public async Task RestoreOriginalAsync_OfficialSuccess_DownloadTempCleaned()
    {
        var gameRoot = CreateGameRoot();
        var officialContent = Encoding.UTF8.GetBytes("content");
        var handler = new MockHttpHandler(officialContent, officialContent.Length);

        var service = CreateService(handler);

        await service.RestoreOriginalAsync();

        var tmpFiles = Directory.GetFiles(_paths.CacheDir, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task RestoreOriginalAsync_OfficialSuccess_GamePatchRecorded()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old"));
        var officialContent = Encoding.UTF8.GetBytes("new");
        var handler = new MockHttpHandler(officialContent, officialContent.Length);

        var service = CreateService(handler, currentOfficialPatch: 42);

        var result = await service.RestoreOriginalAsync();

        Assert.True(result.IsSuccess);

        var stateStore = new InstallationStateStore(_paths, _logger);
        var state = stateStore.Load();
        Assert.Equal(42, state.Value!.GamePatch);
    }

    [Fact]
    public async Task RestoreOriginalAsync_OfficialSuccess_GamePatchNull_StoredAsNull()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("old"));
        var officialContent = Encoding.UTF8.GetBytes("new");
        var handler = new MockHttpHandler(officialContent, officialContent.Length);

        var service = CreateService(handler, currentOfficialPatch: null);

        var result = await service.RestoreOriginalAsync();

        Assert.True(result.IsSuccess);

        var stateStore = new InstallationStateStore(_paths, _logger);
        var state = stateStore.Load();
        Assert.Null(state.Value!.GamePatch);
    }

    // --- Official download fails → fallback ---

    [Fact]
    public async Task RestoreOriginalAsync_DownloadFails_FallbackToSnapshot()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original game"));

        await CreateValidSnapshot(gamePatch: 100, content: Encoding.UTF8.GetBytes("original snapshot"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("original snapshot", File.ReadAllText(GameLocFilePath));
    }

    // --- Fallback: patch mismatch → PatchMismatch ---

    [Fact]
    public async Task RestoreOriginalAsync_PatchMismatch_ReturnsPatchMismatch()
    {
        var gameRoot = CreateGameRoot();

        await CreateValidSnapshot(gamePatch: 99, content: Encoding.UTF8.GetBytes("snapshot content"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.PatchMismatch, result.Error);
    }

    // --- Fallback: snapshot patch null → forbidden ---

    [Fact]
    public async Task RestoreOriginalAsync_SnapshotPatchNull_ReturnsFallbackNotAllowed()
    {
        var gameRoot = CreateGameRoot();

        await CreateValidSnapshot(gamePatch: null, content: Encoding.UTF8.GetBytes("snapshot content"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.FallbackNotAllowed, result.Error);
    }

    // --- Fallback: currentOfficialPatch null → forbidden ---

    [Fact]
    public async Task RestoreOriginalAsync_CurrentPatchNull_ReturnsFallbackNotAllowed()
    {
        var gameRoot = CreateGameRoot();

        await CreateValidSnapshot(gamePatch: 100, content: Encoding.UTF8.GetBytes("snapshot content"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: null);

        var result = await service.RestoreOriginalAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.FallbackNotAllowed, result.Error);
    }

    // --- Fallback: corrupted snapshot → forbidden ---

    [Fact]
    public async Task RestoreOriginalAsync_CorruptedSnapshot_ReturnsFallbackNotAllowed()
    {
        var gameRoot = CreateGameRoot();

        await CreateValidSnapshot(gamePatch: 100, content: Encoding.UTF8.GetBytes("snapshot content"));

        File.WriteAllText(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc"), "corrupted");

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.FallbackNotAllowed, result.Error);
    }

    // --- Fallback does not modify immutable snapshot ---

    [Fact]
    public async Task RestoreOriginalAsync_Fallback_DoesNotModifySnapshot()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("game"));

        await CreateValidSnapshot(gamePatch: 100, content: Encoding.UTF8.GetBytes("immutable snapshot"));

        var originalSnapshotHash = await BdoClient.Services.HashHelper.ComputeFileSha256Async(
            Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        await service.RestoreOriginalAsync();

        var afterHash = await BdoClient.Services.HashHelper.ComputeFileSha256Async(
            Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc"));
        Assert.Equal(originalSnapshotHash, afterHash);
    }

    // --- Failure before replace → target unchanged ---

    [Fact]
    public async Task RestoreOriginalAsync_DownloadFailsNoSnapshot_TargetUnchanged()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("original", File.ReadAllText(GameLocFilePath));
    }

    // --- Source validation: arbitrary .loc outside game ads path ---

    [Fact]
    public async Task CreateOriginalSnapshot_ArbitraryPathOutsideGameAds_Rejected()
    {
        var arbitraryDir = Path.Combine(_tempDir, "arbitrary");
        Directory.CreateDirectory(arbitraryDir);
        File.WriteAllText(Path.Combine(arbitraryDir, "languagedata_en.loc"), "data");

        var store = new BackupStore(_paths, _logger);
        var result = await store.CreateOriginalSnapshotAsync(arbitraryDir, trustedGamePatch: 100);

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SourceMissing, result.Error);
    }

    // --- State-save cancellation after successful replace: recovery then propagate ---

    [Fact]
    public async Task RestoreOriginalAsync_StateSaveCancelled_RecoveryThenPropagate()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original bytes"));
        var officialContent = Encoding.UTF8.GetBytes("official content");
        var handler = new MockHttpHandler(officialContent, officialContent.Length);

        var installer = new LocalizationInstaller(new HttpClient(handler), _paths, _logger);
        var backupStore = new BackupStore(_paths, _logger);
        var stateStore = new InstallationStateStore(_paths, _logger);

        // Inject cancellation on state save: token is already cancelled when SaveAsync is called
        using var cts = new CancellationTokenSource();
        stateStore.OnSaveAsync = (metadata, ct) => throw new OperationCanceledException(ct);

        var service = new RestoreOriginalService(
            installer, backupStore, stateStore, _logger,
            gameRoot: gameRoot,
            officialSourceUrl: "https://example.com/loc.loc",
            currentOfficialPatch: 100);

        // RestoreOriginalAsync: download → restore point → replace → state save (throws OCE)
        // Recovery with independent token restores original bytes, then propagates OCE
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RestoreOriginalAsync());

        stateStore.OnSaveAsync = null;

        // Original bytes restored by recovery (independent CancellationToken.None)
        Assert.Equal("original bytes", File.ReadAllText(GameLocFilePath));
    }

    // --- Fallback: restore point contains pre-operation state snapshot ---

    [Fact]
    public async Task RestoreOriginal_Fallback_CreatesRestorePointWithStateSnapshot()
    {
        var gameRoot = CreateGameRoot(Encoding.UTF8.GetBytes("original game"));

        var stateStore = new InstallationStateStore(_paths, _logger);
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
        await stateStore.SaveAsync(oldMetadata);
        var preOpStateBytes = File.ReadAllBytes(Path.Combine(_paths.StateDir, "installation.json"));

        await CreateValidSnapshot(gamePatch: 100, content: Encoding.UTF8.GetBytes("original snapshot"));

        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var service = CreateService(handler, currentOfficialPatch: 100);

        var result = await service.RestoreOriginalAsync();
        Assert.True(result.IsSuccess);

        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Single(rpDirs);

        var metadataJson = File.ReadAllText(Path.Combine(rpDirs[0], "metadata.json"));
        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(metadataJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("restore_original_fallback", metadata.Source);
        Assert.Equal("present", metadata.InstallationState);

        var stateFilePath = Path.Combine(rpDirs[0], "installation-state.json");
        Assert.True(File.Exists(stateFilePath));
        Assert.Equal(preOpStateBytes, File.ReadAllBytes(stateFilePath));
    }

    // --- MockHttpHandler ---

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

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

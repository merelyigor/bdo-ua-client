using System.Text;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Tests.Storage;

public class BackupStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly NullLogger _logger = new();
    private readonly BackupStore _store;

    public BackupStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
        _store = new BackupStore(_paths, _logger);
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

    // --- Original snapshot: first creation ---

    [Fact]
    public async Task CreateOriginalSnapshot_FirstCall_CreatesFileAndMetadata()
    {
        var gameRoot = CreateGameRoot();

        var result = await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc")));
        Assert.True(File.Exists(Path.Combine(_paths.OriginalBackupDir, "metadata.json")));
    }

    [Fact]
    public async Task CreateOriginalSnapshot_FirstCall_MetadataMatchesFile()
    {
        var content = Encoding.UTF8.GetBytes("test snapshot content");
        var gameRoot = CreateGameRoot(content);

        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        var metadataPath = Path.Combine(_paths.OriginalBackupDir, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(json);

        Assert.NotNull(metadata);
        Assert.Equal(100, metadata.GamePatch);
        Assert.Equal(content.Length, metadata.SizeBytes);

        var actualHash = await BdoClient.Services.HashHelper.ComputeFileSha256Async(
            Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc"));
        Assert.Equal(actualHash, metadata.Sha256);
    }

    [Fact]
    public async Task CreateOriginalSnapshot_GamePatchNull_StoresNull()
    {
        var gameRoot = CreateGameRoot();

        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: null);

        var metadataPath = Path.Combine(_paths.OriginalBackupDir, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(json);

        Assert.NotNull(metadata);
        Assert.Null(metadata.GamePatch);
    }

    // --- Original snapshot: second call does NOT overwrite ---

    [Fact]
    public async Task CreateOriginalSnapshot_SecondCall_DoesNotOverwrite()
    {
        var content1 = Encoding.UTF8.GetBytes("first content");
        var gameRoot = CreateGameRoot(content1);
        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc");
        var originalBytes = File.ReadAllBytes(snapshotPath);

        var adsDir = Path.Combine(gameRoot, "ads");
        File.WriteAllText(Path.Combine(adsDir, "languagedata_en.loc"), "modified content");
        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 200);

        var afterBytes = File.ReadAllBytes(snapshotPath);
        Assert.Equal(originalBytes, afterBytes);

        var metadataPath = Path.Combine(_paths.OriginalBackupDir, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(json);
        Assert.Equal(100, metadata!.GamePatch);
    }

    // --- Original snapshot: source missing ---

    [Fact]
    public async Task CreateOriginalSnapshot_SourceMissing_ReturnsSourceMissing()
    {
        var nonexistentRoot = Path.Combine(_tempDir, "nonexistent");

        var result = await _store.CreateOriginalSnapshotAsync(nonexistentRoot, trustedGamePatch: 100);

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SourceMissing, result.Error);
    }

    // --- Original snapshot: corrupted existing ---

    [Fact]
    public async Task CreateOriginalSnapshot_CorruptedExisting_ReturnsError()
    {
        var gameRoot = CreateGameRoot();
        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc");
        File.WriteAllText(snapshotPath, "corrupted");

        var result = await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SnapshotCorrupted, result.Error);
        Assert.Equal("corrupted", File.ReadAllText(snapshotPath));
    }

    // --- Original snapshot: incomplete (file without metadata) ---

    [Fact]
    public async Task CreateOriginalSnapshot_IncompleteFileNoMetadata_ReturnsError()
    {
        var snapshotDir = _paths.OriginalBackupDir;
        File.WriteAllText(Path.Combine(snapshotDir, "languagedata_en.loc"), "partial");

        var gameRoot = CreateGameRoot();
        var result = await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SnapshotCorrupted, result.Error);
    }

    [Fact]
    public async Task CreateOriginalSnapshot_IncompleteMetadataNoFile_ReturnsError()
    {
        var snapshotDir = _paths.OriginalBackupDir;
        File.WriteAllText(Path.Combine(snapshotDir, "metadata.json"), "{}");

        var gameRoot = CreateGameRoot();
        var result = await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SnapshotCorrupted, result.Error);
    }

    // --- Snapshot atomicity: failure between snapshot and metadata ---

    [Fact]
    public async Task CreateOriginalSnapshot_ExistingCorrupted_NotOverwritten()
    {
        var gameRoot = CreateGameRoot();
        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc");
        var metadataPath = Path.Combine(_paths.OriginalBackupDir, "metadata.json");

        // Pre-create snapshot file without metadata (simulates incomplete prior attempt)
        File.WriteAllText(snapshotPath, "partial");

        var result = await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        // Should fail because existing snapshot detected as corrupted (no metadata)
        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SnapshotCorrupted, result.Error);

        // Corrupted existing snapshot is NOT overwritten or cleaned up
        Assert.True(File.Exists(snapshotPath));
        Assert.Equal("partial", File.ReadAllText(snapshotPath));
        Assert.False(File.Exists(metadataPath));
    }

    // --- CheckOriginalSnapshot ---

    [Fact]
    public async Task CheckOriginalSnapshot_Nothing_ReturnsNotExist()
    {
        var (exists, isValid, error) = await _store.CheckOriginalSnapshotAsync();
        Assert.False(exists);
        Assert.False(isValid);
        Assert.Null(error);
    }

    [Fact]
    public async Task CheckOriginalSnapshot_Valid_ReturnsValid()
    {
        var gameRoot = CreateGameRoot();
        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        var (exists, isValid, error) = await _store.CheckOriginalSnapshotAsync();
        Assert.True(exists);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public async Task CheckOriginalSnapshot_Corrupted_ReturnsInvalid()
    {
        var gameRoot = CreateGameRoot();
        await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100);

        File.WriteAllText(Path.Combine(_paths.OriginalBackupDir, "languagedata_en.loc"), "corrupted");

        var (exists, isValid, error) = await _store.CheckOriginalSnapshotAsync();
        Assert.True(exists);
        Assert.False(isValid);
        Assert.Equal(RestoreError.SnapshotCorrupted, error);
    }

    // --- Restore point ---

    [Fact]
    public async Task CreateRestorePoint_CreatesUniqueDirectory()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        var (rpDir1, result1) = await _store.CreateRestorePointAsync(gameFile, 100, "test1");
        var (rpDir2, result2) = await _store.CreateRestorePointAsync(gameFile, 100, "test2");

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotNull(rpDir1);
        Assert.NotNull(rpDir2);
        Assert.NotEqual(rpDir1, rpDir2);
        Assert.True(Directory.Exists(rpDir1));
        Assert.True(Directory.Exists(rpDir2));
    }

    [Fact]
    public async Task CreateRestorePoint_ContainsFileAndMetadata()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        var (rpDir, result) = await _store.CreateRestorePointAsync(gameFile, 100, "test");

        Assert.True(result.IsSuccess);
        Assert.NotNull(rpDir);
        Assert.True(File.Exists(Path.Combine(rpDir, "languagedata_en.loc")));
        Assert.True(File.Exists(Path.Combine(rpDir, "metadata.json")));
    }

    [Fact]
    public async Task CreateRestorePoint_MetadataHashCorrect()
    {
        var content = Encoding.UTF8.GetBytes("restore point content");
        var gameRoot = CreateGameRoot(content);
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        var (rpDir, result) = await _store.CreateRestorePointAsync(gameFile, 100, "test");

        Assert.True(result.IsSuccess);
        Assert.NotNull(rpDir);

        var metadataPath = Path.Combine(rpDir, "metadata.json");
        var json = File.ReadAllText(metadataPath);
        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(json);

        Assert.NotNull(metadata);
        Assert.Equal(content.Length, metadata.SizeBytes);
        Assert.Equal(100, metadata.GamePatch);
        Assert.Equal("test", metadata.Source);

        var actualHash = await BdoClient.Services.HashHelper.ComputeFileSha256Async(
            Path.Combine(rpDir, "languagedata_en.loc"));
        Assert.Equal(actualHash, metadata.Sha256);
    }

    [Fact]
    public async Task CreateRestorePoint_SourceMissing_ReturnsFailure()
    {
        var (rpDir, result) = await _store.CreateRestorePointAsync(
            Path.Combine(_tempDir, "nonexistent.loc"), 100, "test");

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.SourceMissing, result.Error);
        Assert.Null(rpDir);
    }

    // --- Replace game file: pre-replace failure ---

    [Fact]
    public async Task ReplaceGameFile_PreReplaceFailure_TargetUnchanged()
    {
        var originalContent = Encoding.UTF8.GetBytes("original content");
        var gameRoot = CreateGameRoot(originalContent);
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        var restoreDir = Path.Combine(_tempDir, "rp");
        Directory.CreateDirectory(restoreDir);

        // Source file doesn't exist → pre-replace failure
        var result = await _store.ReplaceGameFileAsync(
            gameFile, Path.Combine(_tempDir, "nonexistent.loc"), restoreDir);

        Assert.False(result.IsSuccess);
        Assert.Equal("original content", File.ReadAllText(gameFile));
    }

    // --- Replace game file: success ---

    [Fact]
    public async Task ReplaceGameFile_Success_ReplacesAndVerifies()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");
        var restoreDir = Path.Combine(_tempDir, "rp");
        Directory.CreateDirectory(restoreDir);

        var sourceContent = Encoding.UTF8.GetBytes("new content");
        var sourceFile = Path.Combine(_tempDir, "source.loc");
        File.WriteAllBytes(sourceFile, sourceContent);

        var result = await _store.ReplaceGameFileAsync(gameFile, sourceFile, restoreDir);

        Assert.True(result.IsSuccess);
        Assert.Equal("new content", File.ReadAllText(gameFile));
    }

    // --- Cancellation leaves no partial ---

    [Fact]
    public async Task CreateOriginalSnapshot_Cancellation_LeavesNoPartial()
    {
        var gameRoot = CreateGameRoot();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await _store.CreateOriginalSnapshotAsync(gameRoot, trustedGamePatch: 100, cts.Token);
        }
        catch (OperationCanceledException) { }

        var snapshotDir = _paths.OriginalBackupDir;
        var files = Directory.GetFiles(snapshotDir);
        Assert.Empty(files);
    }

    [Fact]
    public async Task CreateRestorePoint_Cancellation_LeavesNoPartial()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await _store.CreateRestorePointAsync(gameFile, 100, "test", cts.Token);
        }
        catch (OperationCanceledException) { }

        var rpDirs = Directory.GetDirectories(_paths.RestorePointsDir);
        Assert.Empty(rpDirs);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

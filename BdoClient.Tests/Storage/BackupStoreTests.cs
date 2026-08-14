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

        // Source file doesn't exist → pre-replace failure (targetReplaced = false)
        var result = await _store.ReplaceGameFileAsync(
            gameFile, Path.Combine(_tempDir, "nonexistent.loc"), restoreDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.ReplaceFailed, result.Error);
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

    // --- Post-replace failure: target modified, recovery required ---

    [Fact]
    public async Task ReplaceGameFile_PostReplaceCancellation_RecoversAndPropagates()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        // Create restore point with original content
        var (rpDir, _) = await _store.CreateRestorePointAsync(gameFile, 100, "pre-cancel");
        Assert.NotNull(rpDir);

        // Replace with new content successfully
        var sourceFile = Path.Combine(_tempDir, "source.loc");
        File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("new-content"));
        var replaceResult = await _store.ReplaceGameFileAsync(gameFile, sourceFile, rpDir);
        Assert.True(replaceResult.IsSuccess);
        Assert.Equal("new-content", File.ReadAllText(gameFile));

        // Now set up a second replace with OnPostReplaceHook to cancel AFTER File.Replace
        // Create a restore point from current state ("new-content") so recovery restores it
        var (rpDir2, _) = await _store.CreateRestorePointAsync(gameFile, 100, "pre-cancel-2");
        Assert.NotNull(rpDir2);

        using var cts = new CancellationTokenSource();
        _store.OnPostReplaceHook = () => cts.Cancel();

        var sourceFile2 = Path.Combine(_tempDir, "source2.loc");
        File.WriteAllBytes(sourceFile2, Encoding.UTF8.GetBytes("should-not-persist"));

        // ReplaceGameFileAsync: File.Replace happens (targetReplaced = true),
        // then OnPostReplaceHook cancels token, then verification throws OperationCanceledException
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.ReplaceGameFileAsync(gameFile, sourceFile2, rpDir2, cts.Token));

        _store.OnPostReplaceHook = null;

        // Recovery restored pre-operation bytes (new-content from rpDir2Dir)
        Assert.Equal("new-content", File.ReadAllText(gameFile));
    }

    [Fact]
    public async Task ReplaceGameFile_PostReplaceCancellation_RecoveryFails_ReturnsRecoveryFailed()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        // Replace with new content successfully
        var sourceFile = Path.Combine(_tempDir, "source.loc");
        File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("new-content"));
        var restoreDir = Path.Combine(_tempDir, "rp-fail-cancel");
        Directory.CreateDirectory(restoreDir);
        var replaceResult = await _store.ReplaceGameFileAsync(gameFile, sourceFile, restoreDir);
        Assert.True(replaceResult.IsSuccess);

        // Set up restore point dir WITHOUT the actual file (recovery will fail)
        var rpDir = Path.Combine(_tempDir, "rp-no-data");
        Directory.CreateDirectory(rpDir);
        File.WriteAllText(Path.Combine(rpDir, "metadata.json"),
            System.Text.Json.JsonSerializer.Serialize(new BackupMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                GamePatch = 100,
                Sha256 = "x",
                SizeBytes = 1,
                Source = "empty"
            }));

        using var cts = new CancellationTokenSource();
        _store.OnPostReplaceHook = () => cts.Cancel();

        var sourceFile2 = Path.Combine(_tempDir, "source2.loc");
        File.WriteAllBytes(sourceFile2, Encoding.UTF8.GetBytes("should-not-persist"));

        // File.Replace happens, then cancellation, then recovery fails (no restore file)
        var result = await _store.ReplaceGameFileAsync(gameFile, sourceFile2, rpDir, cts.Token);

        _store.OnPostReplaceHook = null;

        // Post-replace cancellation + recovery failed → RecoveryFailed
        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.RecoveryFailed, result.Error);
    }

    [Fact]
    public async Task ReplaceGameFile_PostReplaceGenericException_RecoveryRestores()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        // Create restore point with original content
        var (rpDir, _) = await _store.CreateRestorePointAsync(gameFile, 100, "pre-exception");
        Assert.NotNull(rpDir);

        // Replace with new content successfully
        var sourceFile = Path.Combine(_tempDir, "source.loc");
        File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("exception-test"));
        var replaceResult = await _store.ReplaceGameFileAsync(gameFile, sourceFile, rpDir);
        Assert.True(replaceResult.IsSuccess);
        Assert.Equal("exception-test", File.ReadAllText(gameFile));

        // OnPostReplaceHook throws generic Exception AFTER File.Replace (targetReplaced = true)
        _store.OnPostReplaceHook = () => throw new IOException("simulated post-replace IO error");

        var sourceFile2 = Path.Combine(_tempDir, "source2.loc");
        File.WriteAllBytes(sourceFile2, Encoding.UTF8.GetBytes("should-not-apply"));

        var result = await _store.ReplaceGameFileAsync(gameFile, sourceFile2, rpDir);

        _store.OnPostReplaceHook = null;

        // Post-replace exception + recovery succeeded → ReplaceFailed
        Assert.False(result.IsSuccess);
        Assert.Equal(RestoreError.ReplaceFailed, result.Error);
        // Target restored from restore point (original content)
        Assert.Equal("game content", File.ReadAllText(gameFile));
    }

    [Fact]
    public async Task RecoverFromRestorePoint_MissingRestoreFile_ReturnsFailure()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        // Replace with new content
        var sourceFile = Path.Combine(_tempDir, "source.loc");
        File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("will-fail"));
        var restoreDir = Path.Combine(_tempDir, "rp-fail");
        Directory.CreateDirectory(restoreDir);
        var replaceResult = await _store.ReplaceGameFileAsync(gameFile, sourceFile, restoreDir);
        Assert.True(replaceResult.IsSuccess);

        // Create restore point directory WITHOUT the actual file
        var rpDir = Path.Combine(_tempDir, "rp-no-file");
        Directory.CreateDirectory(rpDir);
        File.WriteAllText(Path.Combine(rpDir, "metadata.json"),
            System.Text.Json.JsonSerializer.Serialize(new BackupMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                GamePatch = 100,
                Sha256 = "x",
                SizeBytes = 1,
                Source = "empty"
            }));

        // Recovery from missing restore point file → failure
        var recoveryResult = await _store.RecoverFromRestorePointAsync(gameFile, rpDir);

        Assert.False(recoveryResult.IsSuccess);
        Assert.Equal(RestoreError.RecoveryFailed, recoveryResult.Error);
    }

    // --- Post-replace cancellation: recovery then propagate ---

    [Fact]
    public async Task ReplaceGameFile_PreReplaceCancellation_TargetUnchanged()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");
        var restoreDir = Path.Combine(_tempDir, "rp-cancel");
        Directory.CreateDirectory(restoreDir);

        // Pre-cancelled token: targetReplaced = false → OperationCanceledException before File.Replace
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var sourceFile = Path.Combine(_tempDir, "source.loc");
            File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("should-not-apply"));
            await _store.ReplaceGameFileAsync(gameFile, sourceFile, restoreDir, cts.Token);
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException) { }

        // Target unchanged (pre-replace cancellation, targetReplaced = false)
        Assert.Equal("game content", File.ReadAllText(gameFile));
    }

    // --- Recovery uses independent token ---

    [Fact]
    public async Task RecoverFromRestorePoint_IndependentToken_Succeeds()
    {
        var gameRoot = CreateGameRoot();
        var gameFile = Path.Combine(gameRoot, "ads", "languagedata_en.loc");

        // Create restore point with original content
        var (rpDir, _) = await _store.CreateRestorePointAsync(gameFile, 100, "pre-independent");
        Assert.NotNull(rpDir);

        // Replace with new content
        var sourceFile = Path.Combine(_tempDir, "source.loc");
        File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("independent-test"));
        var replaceResult = await _store.ReplaceGameFileAsync(gameFile, sourceFile, rpDir);
        Assert.True(replaceResult.IsSuccess);
        Assert.Equal("independent-test", File.ReadAllText(gameFile));

        // Recovery with CancellationToken.None restores original content
        var recoveryResult = await _store.RecoverFromRestorePointAsync(gameFile, rpDir, CancellationToken.None);

        Assert.True(recoveryResult.IsSuccess);
        Assert.Equal("game content", File.ReadAllText(gameFile));
    }

    // --- Recovery failure: returns RecoveryFailed ---

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

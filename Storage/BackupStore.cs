using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Storage;

public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string SnapshotFile = "languagedata_en.loc";
    private const string MetadataFile = "metadata.json";

    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public BackupStore(AppPaths paths, ILogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // --- Original snapshot ---

    public async Task<(bool exists, bool isValid, RestoreError? error)> CheckOriginalSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, SnapshotFile);
        var metadataPath = Path.Combine(_paths.OriginalBackupDir, MetadataFile);

        var hasFile = File.Exists(snapshotPath);
        var hasMetadata = File.Exists(metadataPath);

        if (!hasFile && !hasMetadata)
            return (false, false, null);

        if (!hasFile || !hasMetadata)
            return (true, false, RestoreError.SnapshotCorrupted);

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, JsonOptions);
            if (metadata == null)
                return (true, false, RestoreError.SnapshotCorrupted);

            var fileInfo = new FileInfo(snapshotPath);
            if (metadata.SizeBytes != fileInfo.Length)
                return (true, false, RestoreError.SnapshotCorrupted);

            var actualSha256 = await HashHelper.ComputeFileSha256Async(snapshotPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(metadata.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
                return (true, false, RestoreError.SnapshotCorrupted);

            return (true, true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (true, false, RestoreError.SnapshotCorrupted);
        }
    }

    public async Task<RestoreResult> CreateOriginalSnapshotAsync(
        string gameRoot, int? trustedGamePatch, CancellationToken cancellationToken = default)
    {
        var sourceGameFilePath = Path.Combine(gameRoot, "ads", SnapshotFile);

        var (exists, isValid, _) = await CheckOriginalSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (exists && isValid)
        {
            _logger.Info("Original snapshot already exists and is valid, skipping");
            return RestoreResult.Success();
        }

        if (exists && !isValid)
        {
            _logger.Error("Original snapshot exists but is corrupted");
            return RestoreResult.Failure(RestoreError.SnapshotCorrupted,
                "Original snapshot exists but is corrupted");
        }

        if (!File.Exists(sourceGameFilePath))
        {
            _logger.Error($"Source game file not found: {sourceGameFilePath}");
            return RestoreResult.Failure(RestoreError.SourceMissing,
                $"Source file not found: {sourceGameFilePath}");
        }

        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, SnapshotFile);
        var metadataPath = Path.Combine(_paths.OriginalBackupDir, MetadataFile);
        var tempPath = snapshotPath + ".tmp";
        var tempMetadataPath = metadataPath + ".tmp";

        CleanupFile(tempPath);
        CleanupFile(tempMetadataPath);

        try
        {
            await HashHelper.CopyFileAsync(sourceGameFilePath, tempPath, cancellationToken).ConfigureAwait(false);

            var sha256 = await HashHelper.ComputeFileSha256Async(tempPath, cancellationToken).ConfigureAwait(false);
            var sizeBytes = new FileInfo(tempPath).Length;

            var metadata = new BackupMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                GamePatch = trustedGamePatch,
                Sha256 = sha256,
                SizeBytes = sizeBytes,
                Source = "original_snapshot"
            };

            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(tempMetadataPath, json, cancellationToken).ConfigureAwait(false);

            // Atomic pair: move snapshot first, then metadata.
            // If metadata move fails, snapshot without metadata is acceptable
            // because CheckOriginalSnapshot will detect incomplete pair.
            File.Move(tempPath, snapshotPath, overwrite: false);
            File.Move(tempMetadataPath, metadataPath, overwrite: false);

            _logger.Info($"Original snapshot created: {snapshotPath} ({sizeBytes} bytes, SHA-256 OK)");
            return RestoreResult.Success();
        }
        catch (OperationCanceledException)
        {
            CleanupFile(tempPath);
            CleanupFile(tempMetadataPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create original snapshot: {ex.Message}");
            CleanupFile(tempPath);
            CleanupFile(tempMetadataPath);
            // Cleanup final artifacts only if they were just created by this failed operation.
            // Pre-existing corrupted snapshot is NOT removed.
            CleanupFile(snapshotPath);
            CleanupFile(metadataPath);
            return RestoreResult.Failure(RestoreError.BackupIo, ex.Message);
        }
    }

    public async Task<(string? snapshotPath, BackupMetadata? metadata, RestoreError? error)> LoadOriginalSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, SnapshotFile);
        var metadataPath = Path.Combine(_paths.OriginalBackupDir, MetadataFile);

        if (!File.Exists(snapshotPath) || !File.Exists(metadataPath))
            return (null, null, RestoreError.SnapshotCorrupted);

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, JsonOptions);
            if (metadata == null)
                return (null, null, RestoreError.SnapshotCorrupted);

            var fileInfo = new FileInfo(snapshotPath);
            if (metadata.SizeBytes != fileInfo.Length)
                return (null, null, RestoreError.SnapshotCorrupted);

            var actualSha256 = await HashHelper.ComputeFileSha256Async(snapshotPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(metadata.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
                return (null, null, RestoreError.SnapshotCorrupted);

            return (snapshotPath, metadata, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, null, RestoreError.SnapshotCorrupted);
        }
    }

    // --- Restore points ---

    public async Task<(string? restorePointDir, RestoreResult result)> CreateRestorePointAsync(
        string gameFilePath, int? gamePatch, string? operationLabel, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gameFilePath))
        {
            return (null, RestoreResult.Failure(RestoreError.SourceMissing,
                $"Game file not found: {gameFilePath}"));
        }

        var dirName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}".Substring(0, 35);
        var restorePointDir = Path.Combine(_paths.RestorePointsDir, dirName);
        var fileCopyPath = Path.Combine(restorePointDir, SnapshotFile);
        var metadataPath = Path.Combine(restorePointDir, MetadataFile);
        var tempCopyPath = fileCopyPath + ".tmp";
        var tempMetadataPath = metadataPath + ".tmp";

        try
        {
            Directory.CreateDirectory(restorePointDir);

            await HashHelper.CopyFileAsync(gameFilePath, tempCopyPath, cancellationToken).ConfigureAwait(false);

            var sha256 = await HashHelper.ComputeFileSha256Async(tempCopyPath, cancellationToken).ConfigureAwait(false);
            var sizeBytes = new FileInfo(tempCopyPath).Length;

            var metadata = new BackupMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                GamePatch = gamePatch,
                Sha256 = sha256,
                SizeBytes = sizeBytes,
                Source = operationLabel ?? "restore_point"
            };

            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(tempMetadataPath, json, cancellationToken).ConfigureAwait(false);

            File.Move(tempCopyPath, fileCopyPath, overwrite: false);
            File.Move(tempMetadataPath, metadataPath, overwrite: false);

            _logger.Info($"Restore point created: {dirName} ({sizeBytes} bytes)");
            return (restorePointDir, RestoreResult.Success());
        }
        catch (OperationCanceledException)
        {
            CleanupFile(tempCopyPath);
            CleanupFile(tempMetadataPath);
            CleanupDirectory(restorePointDir);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create restore point: {ex.Message}");
            CleanupFile(tempCopyPath);
            CleanupFile(tempMetadataPath);
            CleanupDirectory(restorePointDir);
            return (null, RestoreResult.Failure(RestoreError.BackupIo, ex.Message));
        }
    }

    // --- Replace game file ---

    public async Task<RestoreResult> ReplaceGameFileAsync(
        string targetPath, string sourceFilePath, string restorePointDir, CancellationToken cancellationToken = default)
    {
        var tempTargetPath = targetPath + ".tmp";
        var targetExistedBefore = File.Exists(targetPath);
        CleanupFile(tempTargetPath);

        try
        {
            // Phase 1: copy source → temp (pre-replace)
            await HashHelper.CopyFileAsync(sourceFilePath, tempTargetPath, cancellationToken).ConfigureAwait(false);

            var expectedSha256 = await HashHelper.ComputeFileSha256Async(tempTargetPath, cancellationToken).ConfigureAwait(false);
            var expectedSize = new FileInfo(tempTargetPath).Length;

            // Phase 2: replace target with temp (post-replace begins here)
            if (targetExistedBefore)
                File.Replace(tempTargetPath, targetPath, null);
            else
                File.Move(tempTargetPath, targetPath, overwrite: false);

            // Phase 3: verify replaced target
            var actualSha256 = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Post-replace verification failed: SHA-256 mismatch");
                var recoveryResult = await RecoverFromRestorePointAsync(targetPath, restorePointDir, cancellationToken).ConfigureAwait(false);
                if (!recoveryResult.IsSuccess)
                    return RestoreResult.Failure(RestoreError.RecoveryFailed,
                        "Post-replace verification failed and recovery also failed");
                return RestoreResult.Failure(RestoreError.VerificationFailed,
                    "Post-replace SHA-256 verification failed");
            }

            _logger.Info($"Game file replaced: {targetPath}");
            return RestoreResult.Success();
        }
        catch (OperationCanceledException)
        {
            CleanupFile(tempTargetPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to replace game file: {ex.Message}");
            CleanupFile(tempTargetPath);
            // Post-replace failure: target may be modified, attempt recovery
            var recoveryResult = await RecoverFromRestorePointAsync(targetPath, restorePointDir, cancellationToken).ConfigureAwait(false);
            if (!recoveryResult.IsSuccess)
                return RestoreResult.Failure(RestoreError.RecoveryFailed,
                    $"Replace failed and recovery also failed: {ex.Message}");
            return RestoreResult.Failure(RestoreError.ReplaceFailed, ex.Message);
        }
    }

    // --- Recovery ---

    public async Task<RestoreResult> RecoverFromRestorePointAsync(
        string targetPath, string restorePointDir, CancellationToken cancellationToken = default)
    {
        var restoreFile = Path.Combine(restorePointDir, SnapshotFile);
        if (!File.Exists(restoreFile))
        {
            _logger.Error("Recovery failed: restore point file not found");
            return RestoreResult.Failure(RestoreError.RecoveryFailed, "Restore point file not found");
        }

        var tempPath = targetPath + ".recovery.tmp";

        try
        {
            await HashHelper.CopyFileAsync(restoreFile, tempPath, cancellationToken).ConfigureAwait(false);

            var expectedSha256 = await HashHelper.ComputeFileSha256Async(tempPath, cancellationToken).ConfigureAwait(false);

            File.Replace(tempPath, targetPath, null);

            var actualSha256 = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Recovery verification failed: SHA-256 mismatch after restore");
                return RestoreResult.Failure(RestoreError.RecoveryFailed, "Recovery target verification failed");
            }

            _logger.Info($"Recovery from restore point succeeded: {targetPath}");
            return RestoreResult.Success();
        }
        catch (OperationCanceledException)
        {
            CleanupFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Recovery from restore point failed: {ex.Message}");
            CleanupFile(tempPath);
            return RestoreResult.Failure(RestoreError.RecoveryFailed, ex.Message);
        }
    }

    private void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup file {path}: {ex.Message}");
        }
    }

    private void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup directory {path}: {ex.Message}");
        }
    }
}

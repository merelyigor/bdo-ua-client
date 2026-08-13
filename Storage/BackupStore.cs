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

    public (bool exists, bool isValid, RestoreError? error) CheckOriginalSnapshot()
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
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, JsonOptions);
            if (metadata == null)
                return (true, false, RestoreError.SnapshotCorrupted);

            var fileInfo = new FileInfo(snapshotPath);
            if (metadata.SizeBytes != fileInfo.Length)
                return (true, false, RestoreError.SnapshotCorrupted);

            var actualSha256 = HashHelper.ComputeFileSha256(snapshotPath);
            if (!string.Equals(metadata.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
                return (true, false, RestoreError.SnapshotCorrupted);

            return (true, true, null);
        }
        catch
        {
            return (true, false, RestoreError.SnapshotCorrupted);
        }
    }

    public async Task<RestoreResult> CreateOriginalSnapshotAsync(
        string sourceGameFilePath, int? trustedGamePatch, CancellationToken cancellationToken = default)
    {
        var (exists, isValid, _) = CheckOriginalSnapshot();
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
            File.Copy(sourceGameFilePath, tempPath, overwrite: false);

            var sha256 = HashHelper.ComputeFileSha256(tempPath);
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

            File.Move(tempPath, snapshotPath, overwrite: false);
            File.Move(tempMetadataPath, metadataPath, overwrite: false);

            _logger.Info($"Original snapshot created: {snapshotPath} ({sizeBytes} bytes, SHA-256 OK)");
            return RestoreResult.Success();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create original snapshot: {ex.Message}");
            CleanupFile(tempPath);
            CleanupFile(tempMetadataPath);
            return RestoreResult.Failure(RestoreError.BackupIo, ex.Message);
        }
    }

    public (string? snapshotPath, BackupMetadata? metadata, RestoreError? error) LoadOriginalSnapshot()
    {
        var snapshotPath = Path.Combine(_paths.OriginalBackupDir, SnapshotFile);
        var metadataPath = Path.Combine(_paths.OriginalBackupDir, MetadataFile);

        if (!File.Exists(snapshotPath) || !File.Exists(metadataPath))
            return (null, null, RestoreError.SnapshotCorrupted);

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, JsonOptions);
            if (metadata == null)
                return (null, null, RestoreError.SnapshotCorrupted);

            var fileInfo = new FileInfo(snapshotPath);
            if (metadata.SizeBytes != fileInfo.Length)
                return (null, null, RestoreError.SnapshotCorrupted);

            var actualSha256 = HashHelper.ComputeFileSha256(snapshotPath);
            if (!string.Equals(metadata.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
                return (null, null, RestoreError.SnapshotCorrupted);

            return (snapshotPath, metadata, null);
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

        try
        {
            Directory.CreateDirectory(restorePointDir);

            File.Copy(gameFilePath, tempCopyPath, overwrite: false);

            var sha256 = HashHelper.ComputeFileSha256(tempCopyPath);
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
            var tempMetadataPath = metadataPath + ".tmp";
            await File.WriteAllTextAsync(tempMetadataPath, json, cancellationToken).ConfigureAwait(false);

            File.Move(tempCopyPath, fileCopyPath, overwrite: false);
            File.Move(tempMetadataPath, metadataPath, overwrite: false);

            _logger.Info($"Restore point created: {dirName} ({sizeBytes} bytes)");
            return (restorePointDir, RestoreResult.Success());
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create restore point: {ex.Message}");
            CleanupFile(tempCopyPath);
            CleanupDirectory(restorePointDir);
            return (null, RestoreResult.Failure(RestoreError.BackupIo, ex.Message));
        }
    }

    // --- Replace game file ---

    public RestoreResult ReplaceGameFile(string targetPath, string sourceFilePath, string restorePointDir)
    {
        var tempTargetPath = targetPath + ".tmp";

        CleanupFile(tempTargetPath);

        try
        {
            File.Copy(sourceFilePath, tempTargetPath, overwrite: false);

            var expectedSha256 = HashHelper.ComputeFileSha256(tempTargetPath);

            File.Move(tempTargetPath, targetPath, overwrite: true);

            var actualSha256 = HashHelper.ComputeFileSha256(targetPath);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Post-replace verification failed: SHA-256 mismatch");
                TryRecoverFromRestorePointPublic(targetPath, restorePointDir);
                return RestoreResult.Failure(RestoreError.VerificationFailed,
                    "Post-replace SHA-256 verification failed");
            }

            _logger.Info($"Game file replaced: {targetPath}");
            return RestoreResult.Success();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to replace game file: {ex.Message}");
            CleanupFile(tempTargetPath);
            TryRecoverFromRestorePointPublic(targetPath, restorePointDir);
            return RestoreResult.Failure(RestoreError.ReplaceFailed, ex.Message);
        }
    }

    public void TryRecoverFromRestorePointPublic(string targetPath, string restorePointDir)
    {
        try
        {
            var restoreFile = Path.Combine(restorePointDir, SnapshotFile);
            if (!File.Exists(restoreFile))
            {
                _logger.Error("Recovery failed: restore point file not found");
                return;
            }

            File.Copy(restoreFile, targetPath, overwrite: true);
            _logger.Info($"Recovery from restore point succeeded: {targetPath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Recovery from restore point failed: {ex.Message}");
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

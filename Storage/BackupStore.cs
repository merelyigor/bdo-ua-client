using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Storage;

public class BackupStore
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

    // Test seam: called between File.Replace and post-replace verification.
    // When set, tests inject cancellation/failure at the destructive boundary.
    internal Action? OnPostReplaceHook { get; set; }

    internal string OriginalBackupDir => _paths.OriginalBackupDir;
    internal string RestorePointsDir => _paths.RestorePointsDir;

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
        catch (Exception ex)
        {
            _logger.Warning($"Original snapshot integrity check failed: {ex.Message}");
            return (true, false, RestoreError.SnapshotCorrupted);
        }
    }

    public virtual async Task<RestoreResult> CreateOriginalSnapshotAsync(
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
            // If metadata move fails → cleanup both files. Incomplete pair is NOT acceptable.
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
        catch (Exception ex)
        {
            _logger.Warning($"Original snapshot load failed: {ex.Message}");
            return (null, null, RestoreError.SnapshotCorrupted);
        }
    }

    // --- Restore points ---

    public virtual async Task<(string? restorePointDir, RestoreResult result)> CreateRestorePointAsync(
        string gameFilePath, int? gamePatch, string? operationLabel,
        byte[]? preOperationStateBytes = null, bool stateWasPresent = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gameFilePath))
        {
            return (null, RestoreResult.Failure(RestoreError.SourceMissing,
                $"Game file not found: {gameFilePath}"));
        }

        if (stateWasPresent && preOperationStateBytes == null)
        {
            return (null, RestoreResult.Failure(RestoreError.BackupIo,
                "Contradictory input: stateWasPresent=true but preOperationStateBytes is null"));
        }

        if (!stateWasPresent && preOperationStateBytes != null)
        {
            return (null, RestoreResult.Failure(RestoreError.BackupIo,
                "Contradictory input: stateWasPresent=false but preOperationStateBytes is not null"));
        }

        var dirName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}".Substring(0, 35);
        var restorePointDir = Path.Combine(_paths.RestorePointsDir, dirName);
        var fileCopyPath = Path.Combine(restorePointDir, SnapshotFile);
        var metadataPath = Path.Combine(restorePointDir, MetadataFile);
        var stateSnapshotPath = Path.Combine(restorePointDir, "installation-state.json");
        var tempCopyPath = fileCopyPath + ".tmp";
        var tempMetadataPath = metadataPath + ".tmp";
        var tempStatePath = stateSnapshotPath + ".tmp";

        string installationStateMarker = stateWasPresent ? "present" : "absent";

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
                Source = operationLabel ?? "restore_point",
                InstallationState = installationStateMarker
            };

            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(tempMetadataPath, json, cancellationToken).ConfigureAwait(false);

            File.Move(tempCopyPath, fileCopyPath, overwrite: false);
            File.Move(tempMetadataPath, metadataPath, overwrite: false);

            if (stateWasPresent && preOperationStateBytes != null)
            {
                await File.WriteAllBytesAsync(tempStatePath, preOperationStateBytes, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(tempStatePath, stateSnapshotPath, overwrite: false);
            }

            _logger.Info($"Restore point created: {dirName} ({sizeBytes} bytes, state={installationStateMarker})");
            return (restorePointDir, RestoreResult.Success());
        }
        catch (OperationCanceledException)
        {
            CleanupFile(tempCopyPath);
            CleanupFile(tempMetadataPath);
            CleanupFile(tempStatePath);
            CleanupDirectory(restorePointDir);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create restore point: {ex.Message}");
            CleanupFile(tempCopyPath);
            CleanupFile(tempMetadataPath);
            CleanupFile(tempStatePath);
            CleanupDirectory(restorePointDir);
            return (null, RestoreResult.Failure(RestoreError.BackupIo, ex.Message));
        }
    }

    // --- Restore point catalog ---

    public async Task<List<RestorePointInfo>> ListRestorePointsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RestorePointInfo>();

        if (!Directory.Exists(_paths.RestorePointsDir))
            return result;

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(_paths.RestorePointsDir);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to enumerate restore points: {ex.Message}");
            return result;
        }

        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
        Array.Reverse(directories);

        foreach (var dir in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = await LoadRestorePointInfoAsync(dir, cancellationToken).ConfigureAwait(false);
                if (info != null)
                    result.Add(info);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Skipping corrupt restore point {Path.GetFileName(dir)}: {ex.Message}");
            }
        }

        return result;
    }

    internal async Task<RestorePointInfo?> LoadRestorePointInfoAsync(
        string restorePointDir, CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(restorePointDir, MetadataFile);
        var gameFilePath = Path.Combine(restorePointDir, SnapshotFile);
        var stateFilePath = Path.Combine(restorePointDir, "installation-state.json");

        if (!File.Exists(metadataPath) || !File.Exists(gameFilePath))
            return null;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, JsonOptions);
        if (metadata == null)
            return null;

        var fileInfo = new FileInfo(gameFilePath);
        if (metadata.SizeBytes != fileInfo.Length)
            return null;

        var actualSha256 = await HashHelper.ComputeFileSha256Async(gameFilePath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(metadata.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            return null;

        bool hasStateFile = File.Exists(stateFilePath);
        bool isRestorable;

        if (metadata.InstallationState == "present")
        {
            isRestorable = hasStateFile;
        }
        else if (metadata.InstallationState == "absent")
        {
            isRestorable = !hasStateFile;
        }
        else if (metadata.InstallationState == null)
        {
            isRestorable = hasStateFile;
        }
        else
        {
            isRestorable = false;
        }

        var dirName = Path.GetFileName(restorePointDir);

        return new RestorePointInfo
        {
            Id = dirName,
            CreatedAt = metadata.CreatedAt,
            GamePatch = metadata.GamePatch,
            Source = metadata.Source,
            SizeBytes = metadata.SizeBytes,
            Sha256 = metadata.Sha256,
            HasInstallationState = hasStateFile,
            IsRestorable = isRestorable
        };
    }

    public async Task<(string? restorePointDir, BackupMetadata? metadata, RestoreError? error)> ResolveRestorePointAsync(
        string restorePointId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(restorePointId))
            return (null, null, RestoreError.RestorePointNotFound);

        if (restorePointId.Contains("..") || restorePointId.Contains('/') || restorePointId.Contains('\\')
            || Path.IsPathRooted(restorePointId))
            return (null, null, RestoreError.RestorePointNotFound);

        var restorePointDir = Path.Combine(_paths.RestorePointsDir, restorePointId);
        var normalizedDir = Path.GetFullPath(restorePointDir);
        var normalizedBase = Path.GetFullPath(_paths.RestorePointsDir);

        if (!normalizedDir.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !normalizedDir.StartsWith(normalizedBase + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return (null, null, RestoreError.RestorePointNotFound);

        if (!Directory.Exists(restorePointDir))
            return (null, null, RestoreError.RestorePointNotFound);

        var info = await LoadRestorePointInfoAsync(restorePointDir, cancellationToken).ConfigureAwait(false);
        if (info == null)
            return (null, null, RestoreError.RestorePointInvalid);

        if (!info.IsRestorable)
            return (null, null, RestoreError.RestorePointInvalid);

        var metadataPath = Path.Combine(restorePointDir, MetadataFile);
        var metadataJson = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson, JsonOptions);

        return (restorePointDir, metadata, null);
    }

    // --- Replace game file ---

    public virtual async Task<RestoreResult> ReplaceGameFileAsync(
        string targetPath, string sourceFilePath, string restorePointDir, CancellationToken cancellationToken = default)
    {
        var tempTargetPath = targetPath + ".tmp";
        var targetExistedBefore = File.Exists(targetPath);
        CleanupFile(tempTargetPath);

        bool targetReplaced = false;

        try
        {
            // Phase 1: copy source → temp (PRE-REPLACE)
            await HashHelper.CopyFileAsync(sourceFilePath, tempTargetPath, cancellationToken).ConfigureAwait(false);

            var expectedSha256 = await HashHelper.ComputeFileSha256Async(tempTargetPath, cancellationToken).ConfigureAwait(false);

            // Phase 2: replace target (POST-REPLACE boundary)
            if (targetExistedBefore)
                File.Replace(tempTargetPath, targetPath, null);
            else
                File.Move(tempTargetPath, targetPath, overwrite: false);

            targetReplaced = true;

            // Test seam: inject cancellation/failure after destructive boundary
            OnPostReplaceHook?.Invoke();

            // Phase 3: verify replaced target
            var actualSha256 = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Post-replace verification failed: SHA-256 mismatch");
                var recoveryResult = await RecoverFromRestorePointAsync(targetPath, restorePointDir, CancellationToken.None).ConfigureAwait(false);
                if (!recoveryResult.IsSuccess)
                    return RestoreResult.Failure(RestoreError.RecoveryFailed,
                        "Post-replace verification failed and recovery also failed");
                return RestoreResult.Failure(RestoreError.VerificationFailed,
                    "Post-replace SHA-256 verification failed");
            }

            _logger.Info($"Game file replaced: {targetPath}");
            return RestoreResult.Success();
        }
        catch (OperationCanceledException) when (!targetReplaced)
        {
            // PRE-REPLACE cancellation: target untouched
            CleanupFile(tempTargetPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            // POST-REPLACE cancellation: target may be modified
            CleanupFile(tempTargetPath);
            var recoveryResult = await RecoverFromRestorePointAsync(targetPath, restorePointDir, CancellationToken.None).ConfigureAwait(false);
            if (!recoveryResult.IsSuccess)
                return RestoreResult.Failure(RestoreError.RecoveryFailed,
                    "Post-replace cancellation and recovery also failed");
            throw;
        }
        catch (Exception ex) when (!targetReplaced)
        {
            // PRE-REPLACE failure: target untouched, no recovery needed
            _logger.Error($"Failed to replace game file (pre-replace): {ex.Message}");
            CleanupFile(tempTargetPath);
            return RestoreResult.Failure(RestoreError.ReplaceFailed, ex.Message);
        }
        catch (Exception ex)
        {
            // POST-REPLACE failure: target may be modified
            _logger.Error($"Failed to replace game file (post-replace): {ex.Message}");
            CleanupFile(tempTargetPath);
            var recoveryResult = await RecoverFromRestorePointAsync(targetPath, restorePointDir, CancellationToken.None).ConfigureAwait(false);
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

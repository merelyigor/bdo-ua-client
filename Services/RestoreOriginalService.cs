using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class RestoreOriginalService
{
    private readonly LocalizationInstaller _installer;
    private readonly BackupStore _backupStore;
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly string _gameLocFilePath;
    private readonly string _officialSourceUrl;
    private readonly int? _currentOfficialPatch;

    public RestoreOriginalService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameLocFilePath,
        string officialSourceUrl,
        int? currentOfficialPatch)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _backupStore = backupStore ?? throw new ArgumentNullException(nameof(backupStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gameLocFilePath = gameLocFilePath ?? throw new ArgumentNullException(nameof(gameLocFilePath));
        _officialSourceUrl = officialSourceUrl ?? throw new ArgumentNullException(nameof(officialSourceUrl));
        _currentOfficialPatch = currentOfficialPatch;
    }

    public async Task<RestoreResult> RestoreOriginalAsync(CancellationToken cancellationToken = default)
    {
        // Step 1: download official file
        var downloadResult = await _installer
            .DownloadOfficialSourceAsync(_officialSourceUrl, progress: null, cancellationToken)
            .ConfigureAwait(false);

        // Step 2: evaluate download result + fallback
        if (downloadResult.IsSuccess)
        {
            return await ApplyOfficialRestoreAsync(downloadResult.TempFilePath!, cancellationToken).ConfigureAwait(false);
        }

        _logger.Warning($"Official download failed: {downloadResult.Error}");

        // Step 3: evaluate fallback
        return await TryFallbackToOriginalSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<RestoreResult> ApplyOfficialRestoreAsync(
        string officialTempFilePath, CancellationToken cancellationToken)
    {
        try
        {
            // Create restore point before replace
            var (rpDir, rpResult) = await _backupStore
                .CreateRestorePointAsync(_gameLocFilePath, _currentOfficialPatch, "restore_original", cancellationToken)
                .ConfigureAwait(false);

            if (!rpResult.IsSuccess || rpDir == null)
            {
                _logger.Error("Failed to create restore point before official restore");
                CleanupDownloadTemp(officialTempFilePath);
                return RestoreResult.Failure(RestoreError.BackupIo,
                    "Failed to create restore point: " + rpResult.ErrorMessage);
            }

            // Replace game file
            var replaceResult = _backupStore.ReplaceGameFile(_gameLocFilePath, officialTempFilePath, rpDir);
            if (!replaceResult.IsSuccess)
            {
                CleanupDownloadTemp(officialTempFilePath);
                return replaceResult;
            }

            // Save installation metadata
            try
            {
                var metadata = new InstallationMetadata
                {
                    Source = "official",
                    ModeSlug = null,
                    PublicId = null,
                    Version = null,
                    Sha256 = null,
                    InstalledAt = DateTimeOffset.UtcNow,
                    GamePatch = _currentOfficialPatch ?? 0
                };
                await _stateStore.SaveAsync(metadata, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save installation metadata after restore: {ex.Message}");
                _backupStore.TryRecoverFromRestorePointPublic(_gameLocFilePath, rpDir);
                return RestoreResult.Failure(RestoreError.StateSaveFailed, ex.Message);
            }

            CleanupDownloadTemp(officialTempFilePath);
            _logger.Info("Restore Original via official source completed successfully");
            return RestoreResult.Success();
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error during official restore: {ex.Message}");
            CleanupDownloadTemp(officialTempFilePath);
            return RestoreResult.Failure(RestoreError.BackupIo, ex.Message);
        }
    }

    private async Task<RestoreResult> TryFallbackToOriginalSnapshotAsync(CancellationToken cancellationToken)
    {
        var (snapshotPath, snapshotMetadata, snapshotError) = _backupStore.LoadOriginalSnapshot();
        if (snapshotPath == null || snapshotMetadata == null || snapshotError != null)
        {
            _logger.Error("Fallback not possible: original snapshot unavailable or corrupted");
            return RestoreResult.Failure(RestoreError.FallbackNotAllowed,
                "Original snapshot unavailable or corrupted");
        }

        if (snapshotMetadata.GamePatch == null)
        {
            _logger.Error("Fallback not possible: snapshot game_patch is unknown");
            return RestoreResult.Failure(RestoreError.FallbackNotAllowed,
                "Snapshot game_patch is unknown");
        }

        if (_currentOfficialPatch == null)
        {
            _logger.Error("Fallback not possible: current official patch is unknown");
            return RestoreResult.Failure(RestoreError.FallbackNotAllowed,
                "Current official patch is unknown");
        }

        if (snapshotMetadata.GamePatch.Value != _currentOfficialPatch.Value)
        {
            _logger.Error($"Fallback not possible: snapshot patch {snapshotMetadata.GamePatch} != current patch {_currentOfficialPatch}");
            return RestoreResult.Failure(RestoreError.PatchMismatch,
                $"Snapshot patch {snapshotMetadata.GamePatch} != current patch {_currentOfficialPatch}");
        }

        return await ApplySnapshotRestoreAsync(snapshotPath, snapshotMetadata, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RestoreResult> ApplySnapshotRestoreAsync(
        string snapshotPath, BackupMetadata snapshotMetadata, CancellationToken cancellationToken)
    {
        // Create restore point before replace
        var (rpDir, rpResult) = await _backupStore
            .CreateRestorePointAsync(_gameLocFilePath, _currentOfficialPatch, "restore_original_fallback", cancellationToken)
            .ConfigureAwait(false);

        if (!rpResult.IsSuccess || rpDir == null)
        {
            _logger.Error("Failed to create restore point before snapshot fallback");
            return RestoreResult.Failure(RestoreError.BackupIo,
                "Failed to create restore point: " + rpResult.ErrorMessage);
        }

        // Replace game file from snapshot
        var replaceResult = _backupStore.ReplaceGameFile(_gameLocFilePath, snapshotPath, rpDir);
        if (!replaceResult.IsSuccess)
            return replaceResult;

        // Save installation metadata
        try
        {
            var metadata = new InstallationMetadata
            {
                Source = "official",
                ModeSlug = null,
                PublicId = null,
                Version = null,
                Sha256 = null,
                InstalledAt = DateTimeOffset.UtcNow,
                GamePatch = _currentOfficialPatch ?? 0
            };
            await _stateStore.SaveAsync(metadata, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save installation metadata after snapshot fallback: {ex.Message}");
            _backupStore.TryRecoverFromRestorePointPublic(_gameLocFilePath, rpDir);
            return RestoreResult.Failure(RestoreError.StateSaveFailed, ex.Message);
        }

        _logger.Info("Restore Original via snapshot fallback completed successfully");
        return RestoreResult.Success();
    }

    private void CleanupDownloadTemp(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup download temp: {ex.Message}");
        }
    }
}

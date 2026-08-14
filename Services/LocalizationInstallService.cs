using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class LocalizationInstallService
{
    private readonly LocalizationInstaller _installer;
    private readonly BackupStore _backupStore;
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly string _gameRoot;
    private readonly string _officialSourceUrl;

    public LocalizationInstallService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot,
        string officialSourceUrl)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _backupStore = backupStore ?? throw new ArgumentNullException(nameof(backupStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gameRoot = gameRoot ?? throw new ArgumentNullException(nameof(gameRoot));
        _officialSourceUrl = officialSourceUrl ?? throw new ArgumentNullException(nameof(officialSourceUrl));
    }

    public async Task<InstallResult> InstallReleaseAsync(
        string modeSlug,
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info($"Install transaction started: mode={modeSlug}, public_id={release.PublicId}");

        // --- Phase 1: Input validation ---
        var gameLocFilePath = Path.Combine(_gameRoot, "ads", "languagedata_en.loc");

        if (!File.Exists(gameLocFilePath))
        {
            _logger.Error($"Game localization file not found: {gameLocFilePath}");
            return InstallResult.Failure(InstallError.InvalidGamePath,
                $"Game localization file not found: {gameLocFilePath}");
        }

        if (string.IsNullOrWhiteSpace(modeSlug))
            return InstallResult.Failure(InstallError.InvalidRelease, "modeSlug is empty");

        if (string.IsNullOrWhiteSpace(release.PublicId))
            return InstallResult.Failure(InstallError.InvalidRelease, "release.PublicId is empty");

        if (release.Version <= 0)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.Version is invalid");

        if (release.Patch <= 0)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.Patch is invalid");

        if (string.IsNullOrWhiteSpace(release.DownloadUrl) || !release.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return InstallResult.Failure(InstallError.InvalidRelease, "release.DownloadUrl is invalid");

        if (release.SizeBytes <= 0)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.SizeBytes is invalid");

        if (string.IsNullOrWhiteSpace(release.Sha256))
            return InstallResult.Failure(InstallError.InvalidRelease, "release.Sha256 is empty");

        if (!release.CompatibleWithOfficialPatch)
        {
            _logger.Error($"Release incompatible: compatible_with_official_patch=false");
            return InstallResult.Failure(InstallError.Incompatible,
                "Release is not compatible with official patch");
        }

        // --- Phase 2: Original snapshot safety ---
        var (snapExists, snapValid, snapError) = await _backupStore
            .CheckOriginalSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (snapExists && !snapValid)
        {
            _logger.Error("Original snapshot corrupted — aborting transaction");
            return InstallResult.Failure(InstallError.OriginalSnapshotFailed,
                "Original snapshot exists but is corrupted");
        }

        if (!snapExists)
        {
            // First install: check for inconsistent state
            var currentState = ReadRawInstallationState();
            if (currentState != null)
            {
                var metadata = System.Text.Json.JsonSerializer.Deserialize<InstallationMetadata>(
                    currentState, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (metadata?.Source == "api")
                {
                    _logger.Error("No original snapshot but source=api metadata exists — aborting (inconsistent state)");
                    return InstallResult.Failure(InstallError.OriginalSnapshotFailed,
                        "Original snapshot missing with existing API installation metadata — inconsistent state");
                }
            }

            // Create original snapshot from current game file
            _logger.Info("Creating original snapshot (first install)");
            var snapResult = await _backupStore
                .CreateOriginalSnapshotAsync(_gameRoot, trustedGamePatch: null, cancellationToken)
                .ConfigureAwait(false);

            if (!snapResult.IsSuccess)
            {
                _logger.Error($"Failed to create original snapshot: {snapResult.Error}");
                return InstallResult.Failure(InstallError.OriginalSnapshotFailed,
                    $"Failed to create original snapshot: {snapResult.ErrorMessage}");
            }
        }

        // --- Phase 3: Capture pre-operation state ---
        var preStateBytes = ReadRawInstallationState();

        // --- Phase 4: Create restore point ---
        string? restorePointDir = null;
        if (File.Exists(gameLocFilePath))
        {
            var rpDir = Path.Combine(
                _backupStore.RestorePointsDir,
                $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}".Substring(0, 35));
            Directory.CreateDirectory(rpDir);

            var rpFile = Path.Combine(rpDir, "languagedata_en.loc");
            await HashHelper.CopyFileAsync(gameLocFilePath, rpFile, cancellationToken).ConfigureAwait(false);

            var rpSha = await HashHelper.ComputeFileSha256Async(rpFile, cancellationToken).ConfigureAwait(false);
            var rpSize = new FileInfo(rpFile).Length;

            var rpMetadata = new BackupMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                GamePatch = null,
                Sha256 = rpSha,
                SizeBytes = rpSize,
                Source = "pre_install"
            };
            var rpJson = System.Text.Json.JsonSerializer.Serialize(rpMetadata,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(rpDir, "metadata.json"), rpJson, cancellationToken)
                .ConfigureAwait(false);

            if (preStateBytes != null)
                await File.WriteAllBytesAsync(Path.Combine(rpDir, "installation-state.json"),
                    preStateBytes, cancellationToken).ConfigureAwait(false);

            restorePointDir = rpDir;
            _logger.Info($"Restore point created: {rpDir}");
        }

        // --- Phase 5: Download ---
        DownloadResult downloadResult;
        try
        {
            downloadResult = await _installer
                .DownloadReleaseAsync(release, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            CleanupFile(restorePointDir);
            throw;
        }

        if (!downloadResult.IsSuccess)
        {
            _logger.Error($"Download failed: {downloadResult.Error}");
            CleanupFile(restorePointDir);
            CleanupDownloadTemp(downloadResult.TempFilePath);
            return InstallResult.Failure(InstallError.DownloadFailed,
                $"Download failed: {downloadResult.ErrorMessage}");
        }

        // --- Phase 6: Replace game file ---
        string downloadTempPath = downloadResult.TempFilePath!;

        try
        {
            var replaceResult = await _backupStore
                .ReplaceGameFileAsync(gameLocFilePath, downloadTempPath, restorePointDir!, cancellationToken)
                .ConfigureAwait(false);

            if (!replaceResult.IsSuccess)
            {
                if (replaceResult.Error == RestoreError.VerificationFailed)
                {
                    _logger.Error("Post-replace verification failed");
                    CleanupDownloadTemp(downloadTempPath);
                    return InstallResult.Failure(InstallError.VerificationFailed,
                        "Post-replace SHA-256 verification failed");
                }

                if (replaceResult.Error == RestoreError.RecoveryFailed)
                {
                    _logger.Error("Post-replace recovery failed — critical");
                    CleanupDownloadTemp(downloadTempPath);
                    return InstallResult.Failure(InstallError.RollbackFailed,
                        "Replace failed and recovery also failed");
                }

                // ReplaceFailed without recovery (pre-replace)
                _logger.Error($"Replace failed: {replaceResult.Error}");
                CleanupDownloadTemp(downloadTempPath);
                CleanupFile(restorePointDir);
                return InstallResult.Failure(InstallError.ReplaceFailed,
                    replaceResult.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Transaction cancelled after replace — rolling back");
            CleanupDownloadTemp(downloadTempPath);
            await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes).ConfigureAwait(false);
            throw;
        }

        // --- Phase 7: Verify installed file ---
        try
        {
            var actualSize = new FileInfo(gameLocFilePath).Length;
            if (actualSize != release.SizeBytes)
            {
                _logger.Error($"Post-replace size mismatch: expected {release.SizeBytes}, got {actualSize}");
                await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes).ConfigureAwait(false);
                CleanupDownloadTemp(downloadTempPath);
                return InstallResult.Failure(InstallError.VerificationFailed,
                    $"Post-replace size mismatch: expected {release.SizeBytes}, got {actualSize}");
            }

            var actualSha = await HashHelper.ComputeFileSha256Async(gameLocFilePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(actualSha, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error($"Post-replace hash mismatch");
                await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes).ConfigureAwait(false);
                CleanupDownloadTemp(downloadTempPath);
                return InstallResult.Failure(InstallError.VerificationFailed,
                    "Post-replace SHA-256 mismatch");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Transaction cancelled during verification — rolling back");
            CleanupDownloadTemp(downloadTempPath);
            await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes).ConfigureAwait(false);
            throw;
        }

        // --- Phase 8: Save installation state ---
        var newMetadata = new InstallationMetadata
        {
            Source = "api",
            ModeSlug = modeSlug,
            PublicId = release.PublicId,
            Version = release.Version,
            GamePatch = release.Patch,
            Sha256 = release.Sha256,
            InstalledAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _stateStore.SaveAsync(newMetadata, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Transaction cancelled during state save — rolling back");
            CleanupDownloadTemp(downloadTempPath);
            await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"State save failed: {ex.Message}");
            CleanupDownloadTemp(downloadTempPath);
            await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes).ConfigureAwait(false);
            return InstallResult.Failure(InstallError.StateSaveFailed, ex.Message);
        }

        // --- Phase 9: Commit ---
        CleanupDownloadTemp(downloadTempPath);
        _logger.Info($"Install transaction completed: mode={modeSlug}, public_id={release.PublicId}");
        return InstallResult.Success();
    }

    private async Task RollbackAsync(string gameLocFilePath, string? restorePointDir, byte[]? preStateBytes)
    {
        _logger.Warning("Rollback initiated");

        // 1. Restore game file
        var gameRollbackOk = false;
        if (restorePointDir != null)
        {
            var rpFile = Path.Combine(restorePointDir, "languagedata_en.loc");
            if (File.Exists(rpFile))
            {
                try
                {
                    var recoveryResult = await _backupStore
                        .RecoverFromRestorePointAsync(gameLocFilePath, restorePointDir, CancellationToken.None)
                        .ConfigureAwait(false);
                    gameRollbackOk = recoveryResult.IsSuccess;
                    if (gameRollbackOk)
                        _logger.Info("Game file rollback succeeded");
                    else
                        _logger.Error($"Game file rollback failed: {recoveryResult.Error}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Game file rollback exception: {ex.Message}");
                }
            }
            else
            {
                _logger.Error("Restore point file missing — cannot rollback game file");
            }
        }
        else
        {
            _logger.Warning("No restore point — cannot rollback game file");
        }

        // 2. Restore installation state
        var stateRollbackOk = false;
        try
        {
            var stateRollbackPath = Path.Combine(_stateStore.StateDir, "installation.json");

            if (preStateBytes == null)
            {
                // Pre-operation state was absent → delete
                if (File.Exists(stateRollbackPath))
                    File.Delete(stateRollbackPath);
                stateRollbackOk = true;
                _logger.Info("Installation state rollback: removed (was absent)");
            }
            else
            {
                // Pre-operation state existed → restore exact bytes
                var tempPath = stateRollbackPath + ".rollback.tmp";
                await File.WriteAllBytesAsync(tempPath, preStateBytes, CancellationToken.None).ConfigureAwait(false);

                if (File.Exists(stateRollbackPath))
                    File.Delete(stateRollbackPath);

                File.Move(tempPath, stateRollbackPath);
                stateRollbackOk = true;
                _logger.Info("Installation state rollback: restored from snapshot");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Installation state rollback failed: {ex.Message}");
        }

        if (!gameRollbackOk || !stateRollbackOk)
            _logger.Error("Rollback partially failed — game state may be inconsistent");

        CleanupFile(restorePointDir);
    }

    private byte[]? ReadRawInstallationState()
    {
        var path = Path.Combine(_stateStore.StateDir, "installation.json");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private void CleanupDownloadTemp(string? tempPath)
    {
        if (tempPath == null) return;
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup download temp: {ex.Message}");
        }
    }

    private void CleanupFile(string? path)
    {
        if (path == null) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup: {ex.Message}");
        }
    }
}

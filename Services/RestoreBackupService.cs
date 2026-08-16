using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class RestoreBackupService
{
    private readonly BackupStore _backupStore;
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly string _gameRoot;

    // Test seam: called after successful game replace, before state apply.
    internal Action? OnPostGameReplaceHook { get; set; }

    public RestoreBackupService(
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot)
    {
        _backupStore = backupStore ?? throw new ArgumentNullException(nameof(backupStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gameRoot = gameRoot ?? throw new ArgumentNullException(nameof(gameRoot));
    }

    public async Task<RestoreResult> RestoreAsync(
        string restorePointId, CancellationToken cancellationToken = default)
    {
        var gameLocFilePath = Path.Combine(_gameRoot, "ads", "languagedata_en.loc");

        if (!File.Exists(gameLocFilePath))
        {
            _logger.Error($"Game localization file not found: {gameLocFilePath}");
            return RestoreResult.Failure(RestoreError.InvalidGamePath,
                $"Game localization file not found: {gameLocFilePath}");
        }

        var (restorePointDir, metadata, resolveError) = await _backupStore
            .ResolveRestorePointAsync(restorePointId, cancellationToken)
            .ConfigureAwait(false);

        if (resolveError != null || restorePointDir == null || metadata == null)
        {
            _logger.Error($"Restore point not found or invalid: {restorePointId}");
            return RestoreResult.Failure(resolveError ?? RestoreError.RestorePointNotFound,
                $"Restore point not found or invalid: {restorePointId}");
        }

        var selectedGameFile = Path.Combine(restorePointDir, "languagedata_en.loc");
        var selectedStateFile = Path.Combine(restorePointDir, "installation-state.json");
        bool hasStateFile = File.Exists(selectedStateFile);

        var stateKind = BackupStore.ClassifyRestorePointState(metadata.InstallationState, hasStateFile);

        if (stateKind == BackupStore.RestorePointStateKind.Invalid)
        {
            _logger.Error($"Restore point has invalid state: marker={metadata.InstallationState}, hasStateFile={hasStateFile}");
            return RestoreResult.Failure(RestoreError.RestorePointInvalid,
                $"Restore point has invalid installation state: marker={metadata.InstallationState}, hasStateFile={hasStateFile}");
        }

        bool stateIsPresent = stateKind == BackupStore.RestorePointStateKind.Present;

        byte[]? selectedStateBytes = null;
        if (stateIsPresent)
        {
            selectedStateBytes = await File.ReadAllBytesAsync(selectedStateFile, cancellationToken)
                .ConfigureAwait(false);
        }

        var preOpStateBytes = ReadRawInstallationState();
        bool preOpStateWasPresent = preOpStateBytes != null;

        var currentLoad = _stateStore.Load();
        int? currentGamePatch = currentLoad.Status == FileLoadStatus.Valid
            ? currentLoad.Value?.GamePatch
            : null;

        var (preRpDir, preRpResult) = await _backupStore
            .CreateRestorePointAsync(gameLocFilePath, currentGamePatch, "pre_restore_backup",
                preOpStateBytes, preOpStateWasPresent, cancellationToken)
            .ConfigureAwait(false);

        if (!preRpResult.IsSuccess || preRpDir == null)
        {
            _logger.Error($"Failed to create pre-operation restore point: {preRpResult.Error}");
            return RestoreResult.Failure(RestoreError.BackupIo,
                "Failed to create pre-operation restore point: " + preRpResult.ErrorMessage);
        }

        var replaceResult = await _backupStore
            .ReplaceGameFileAsync(gameLocFilePath, selectedGameFile, preRpDir, cancellationToken)
            .ConfigureAwait(false);

        if (!replaceResult.IsSuccess)
        {
            _logger.Error($"Game file replace failed: {replaceResult.Error}");
            return replaceResult;
        }

        try
        {
            OnPostGameReplaceHook?.Invoke();

            if (stateIsPresent && selectedStateBytes != null)
            {
                var statePath = Path.Combine(_stateStore.StateDir, "installation.json");
                var tempStatePath = statePath + ".tmp";

                await File.WriteAllBytesAsync(tempStatePath, selectedStateBytes, cancellationToken)
                    .ConfigureAwait(false);

                if (File.Exists(statePath))
                    File.Replace(tempStatePath, statePath, null);
                else
                    File.Move(tempStatePath, statePath, overwrite: false);

                var verifyBytes = await File.ReadAllBytesAsync(statePath, cancellationToken)
                    .ConfigureAwait(false);
                if (verifyBytes.Length != selectedStateBytes.Length
                    || !verifyBytes.AsSpan().SequenceEqual(selectedStateBytes))
                {
                    _logger.Error("Installation state verification failed after restore");
                    var rollbackResult = await RollbackBothAsync(gameLocFilePath, preRpDir, preOpStateBytes, preOpStateWasPresent, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!rollbackResult.IsSuccess)
                        return RestoreResult.Failure(RestoreError.RecoveryFailed,
                            "State verification failed and rollback also failed");
                    return RestoreResult.Failure(RestoreError.StateRestoreFailed,
                        "Installation state verification failed after restore");
                }
            }
            else
            {
                var statePath = Path.Combine(_stateStore.StateDir, "installation.json");
                if (File.Exists(statePath))
                {
                    File.Delete(statePath);
                    if (File.Exists(statePath))
                    {
                        _logger.Error("Failed to delete installation state file for absent-state restore");
                        var rollbackResult = await RollbackBothAsync(gameLocFilePath, preRpDir, preOpStateBytes, preOpStateWasPresent, CancellationToken.None)
                            .ConfigureAwait(false);
                        if (!rollbackResult.IsSuccess)
                            return RestoreResult.Failure(RestoreError.RecoveryFailed,
                                "State delete failed and rollback also failed");
                        return RestoreResult.Failure(RestoreError.StateRestoreFailed,
                            "Failed to remove installation state file");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("State restore cancelled — rolling back");
            var rollbackResult = await RollbackBothAsync(gameLocFilePath, preRpDir, preOpStateBytes, preOpStateWasPresent, CancellationToken.None)
                .ConfigureAwait(false);
            if (!rollbackResult.IsSuccess)
                return RestoreResult.Failure(RestoreError.RecoveryFailed,
                    "State restore cancelled and rollback also failed");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"State restore failed: {ex.Message}");
            var rollbackResult = await RollbackBothAsync(gameLocFilePath, preRpDir, preOpStateBytes, preOpStateWasPresent, CancellationToken.None)
                .ConfigureAwait(false);
            if (!rollbackResult.IsSuccess)
                return RestoreResult.Failure(RestoreError.RecoveryFailed,
                    $"State restore failed and rollback also failed: {ex.Message}");
            return RestoreResult.Failure(RestoreError.StateRestoreFailed, ex.Message);
        }

        _logger.Info($"Restore Backup completed: {restorePointId}");
        return RestoreResult.Success();
    }

    private async Task<RollbackResult> RollbackBothAsync(
        string gameLocFilePath, string preRpDir,
        byte[]? preOpStateBytes, bool preOpStateWasPresent,
        CancellationToken cancellationToken)
    {
        bool gameRestored = false;
        bool stateRestored = false;

        try
        {
            var recoveryResult = await _backupStore
                .RecoverFromRestorePointAsync(gameLocFilePath, preRpDir, cancellationToken)
                .ConfigureAwait(false);
            gameRestored = recoveryResult.IsSuccess;
            if (!gameRestored)
                _logger.Error($"Game rollback failed: {recoveryResult.Error}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Game rollback exception: {ex.Message}");
        }

        try
        {
            stateRestored = await RollbackStateAsync(preOpStateBytes, preOpStateWasPresent, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"State rollback exception: {ex.Message}");
        }

        if (gameRestored && stateRestored)
            return new RollbackResult(true, true, true, null);

        return new RollbackResult(false, gameRestored, stateRestored,
            $"Rollback partially failed: game={gameRestored}, state={stateRestored}");
    }

    private async Task<bool> RollbackStateAsync(
        byte[]? preOpStateBytes, bool preOpStateWasPresent, CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(_stateStore.StateDir, "installation.json");

        if (!preOpStateWasPresent)
        {
            if (!File.Exists(statePath))
                return true;

            File.Delete(statePath);
            return !File.Exists(statePath);
        }

        if (preOpStateBytes == null)
            return false;

        var tempPath = statePath + ".rollback.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, preOpStateBytes, cancellationToken)
                .ConfigureAwait(false);

            if (File.Exists(statePath))
                File.Replace(tempPath, statePath, null);
            else
                File.Move(tempPath, statePath, overwrite: false);

            var restoredBytes = await File.ReadAllBytesAsync(statePath, cancellationToken)
                .ConfigureAwait(false);
            return restoredBytes.Length == preOpStateBytes.Length
                && restoredBytes.AsSpan().SequenceEqual(preOpStateBytes);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private byte[]? ReadRawInstallationState()
    {
        var path = Path.Combine(_stateStore.StateDir, "installation.json");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

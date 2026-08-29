using System.Diagnostics;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{

    private async Task HandleInstallAsync()
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false);
            SetControlsDuringOperation(false);

            if (_gameRoot == null)
            {
                finalMessage = "Гру не знайдено.";
                return;
            }

            if (!_apiLoadedSuccessfully)
            {
                finalMessage = $"Помилка завантаження API: {_apiErrorMessage}";
                return;
            }

            var mode = GetSelectedApiMode();
            if (mode?.Current == null)
            {
                finalMessage = "Актуальний реліз відсутній.";
                return;
            }

            var current = mode.Current;

            var compatResult = _compatService.Check(current);
            if (!compatResult.IsAllowed)
            {
                finalMessage = compatResult.Reason ?? "Операція заблокована.";
                return;
            }

            // Factual state check using INSTALLED mode current
            var installedLoad = _stateStore.Load();
            string? installedModeSlug = null;
            string? installedPublicId = null;
            CurrentRelease? installedModeCurrent = null;

            if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == InstallationSource.Api)
            {
                installedModeSlug = installedLoad.Value.ModeSlug;
                installedPublicId = installedLoad.Value.PublicId;
                var installedApiMode = _apiResponse?.Data?.Modes?
                    .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
                installedModeCurrent = installedApiMode?.Current;
            }

            var gameLocPath = GamePaths.GetLocalizationFilePath(_gameRoot);
            var factualState = await _stateService.ResolveAsync(installedModeCurrent, gameLocPath, gameRoot: _gameRoot);

            // Abort before any install transaction if a real application shutdown
            // became pending while awaiting factual-state resolution (the operation
            // CTS does not exist yet, so cancellation could not have been requested).
            if (_exitAfterOperation || _closing)
            {
                _logger.Info("Install aborted before transaction start because application shutdown is pending.");
                return;
            }

            var policy = InstallActionPolicy.Evaluate(
                factualState.State, installedModeSlug, installedPublicId,
                mode, current, compatResult, operationInProgress: false);

            if (!policy.CanInstall)
            {
                if (policy.AlreadyInstalledExactTarget)
                    finalMessage = "Цей реліз уже встановлено.";
                else
                    finalMessage = "Встановлення недоступне для поточного стану.";
                return;
            }

            SetMessage("Встановлення локалізації...");
            SetProgress(0);
            SetOperationState(OperationState.Downloading);

            _operationCts = new CancellationTokenSource();
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

            var service = new LocalizationInstallService(
                _localizationInstaller, _backupStore, _stateStore, _logger, _gameRoot);

            var progress = new Progress<DownloadProgress>(OnDownloadProgress);

            var result = await service.InstallReleaseAsync(
                mode.Slug!, current, progress, _operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = "Локалізацію успішно встановлено.";
            }
            else
            {
                SetOperationState(OperationState.Failed);
                _logger.Error($"Install failed: {result.Error} — {result.ErrorMessage}");
                var errorText = MapInstallError(result.Error!.Value);

                if (result.Error == InstallError.RollbackFailed)
                    finalMessage = $"КРИТИЧНО: {errorText}";
                else
                    finalMessage = errorText;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Install cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Встановлення скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Install error: {ex.Message}");
            SetOperationState(OperationState.Failed);
            finalMessage = $"Помилка операції: {ex.Message}";
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);

            try
            {
                try
                {
                    await RefreshStateAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Post-operation refresh failed: {ex.Message}");
                    if (finalMessage == null)
                        finalMessage = $"Не вдалося оновити стан: {ex.Message}";
                    else
                        finalMessage += $"{Environment.NewLine}{Environment.NewLine}Не вдалося оновити стан: {ex.Message}";
                }

                if (finalMessage != null)
                    SetMessage(finalMessage);

                await _feedCoordinator.ApplyPendingIfAnyAsync();
            }
            finally
            {
                _feedCoordinator.UnblockUpdates();
                if (!_closing)
                    _poller.Resume();
                CompletePendingExitAfterOperation();
            }
        }
    }
    private async void RestoreOriginalButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleRestoreOriginalAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"RestoreOriginalButton_Click unexpected: {ex.Message}");
            SetMessage($"Помилка: {ex.Message}");
        }
    }
    private async Task HandleRestoreOriginalAsync()
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false);
            SetControlsDuringOperation(false);

            if (_gameRoot == null)
            {
                finalMessage = "Гру не знайдено.";
                return;
            }

            if (!_apiLoadedSuccessfully || _apiResponse?.Data == null)
            {
                finalMessage = "Дані API недоступні для відновлення оригіналу.";
                return;
            }

            var data = _apiResponse.Data;
            var officialSourceUrl = data.OfficialSourceUrl;
            int? officialPatch = data.OfficialPatch > 0 ? data.OfficialPatch : null;

            SetMessage("Відновлення оригінального файлу...");
            SetProgress(0);
            SetOperationState(OperationState.Restoring);

            _operationCts = new CancellationTokenSource();
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

            var service = new RestoreOriginalService(
                _localizationInstaller, _backupStore, _stateStore, _logger,
                _gameRoot, officialSourceUrl ?? "", officialPatch);

            var result = await service.RestoreOriginalAsync(_operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = "Оригінальні файли відновлено.";
            }
            else
            {
                SetOperationState(OperationState.Failed);
                _logger.Error($"Restore original failed: {result.Error} — {result.ErrorMessage}");
                var errorText = MapRestoreError(result.Error!.Value);

                if (result.Error == RestoreError.RecoveryFailed)
                    finalMessage = $"КРИТИЧНО: {errorText}";
                else
                    finalMessage = errorText;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Restore Original cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Відновлення оригіналу скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Restore original error: {ex.Message}");
            SetOperationState(OperationState.Failed);
            finalMessage = $"Помилка відновлення: {ex.Message}";
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);

            try
            {
                try
                {
                    await RefreshStateAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Post-operation refresh failed: {ex.Message}");
                    if (finalMessage == null)
                        finalMessage = $"Не вдалося оновити стан: {ex.Message}";
                    else
                        finalMessage += $"{Environment.NewLine}{Environment.NewLine}Не вдалося оновити стан: {ex.Message}";
                }

                if (finalMessage != null)
                    SetMessage(finalMessage);

                await _feedCoordinator.ApplyPendingIfAnyAsync();
            }
            finally
            {
                _feedCoordinator.UnblockUpdates();
                if (!_closing)
                    _poller.Resume();
                CompletePendingExitAfterOperation();
            }
        }
    }
    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (!_operationInProgress || _operationCts == null)
            return;

        cancelButton.Enabled = false;
        SetMessage("Скасування операції...");
        _operationCts.Cancel();
    }
    private static string MapInstallError(InstallError error) => error switch
    {
        InstallError.InvalidGamePath => "Шлях до гри недійсний або файл локалізації відсутній.",
        InstallError.InvalidRelease => "Метадані релізу пошкоджено або неповні.",
        InstallError.Incompatible => "Реліз не сумісний з поточним офіційним патчем гри.",
        InstallError.DownloadFailed => "Не вдалося завантажити файл локалізації. Перевірте з'єднання з Інтернетом.",
        InstallError.OriginalSnapshotFailed => "Не вдалося створити резервну копію оригінального файлу.",
        InstallError.PreOperationStateFailed => "Стан встановлення пошкоджено. Спробуйте перезапустити програму.",
        InstallError.BackupFailed => "Не вдалося створити точку відновлення.",
        InstallError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        InstallError.VerificationFailed => "Перевірка встановленого файлу не пройдена. Файл може бути пошкоджено.",
        InstallError.StateSaveFailed => "Не вдалося зберегти стан встановлення. Зміни відкочено.",
        InstallError.RollbackFailed => "Не вдалося повністю відкотити зміни. Перевірте файли гри та журнал.",
        _ => "Невідома помилка встановлення."
    };
    private static string MapRestoreError(RestoreError error) => error switch
    {
        RestoreError.InvalidGamePath => "Шлях до гри недійсний або файл локалізації відсутній.",
        RestoreError.SourceMissing => "Вихідний файл відсутній.",
        RestoreError.SnapshotCorrupted => "Резервна копія пошкоджена.",
        RestoreError.BackupIo => "Не вдалося створити резервну копію поточного стану.",
        RestoreError.OfficialDownloadFailed => "Не вдалося завантажити оригінальний файл з сервера.",
        RestoreError.FallbackNotAllowed => "Відновлення з локальної копії неможливе (патч не збігається або копія відсутня).",
        RestoreError.PatchMismatch => "Патч локальної копії не збігається з поточним офіційним патчем.",
        RestoreError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        RestoreError.VerificationFailed => "Перевірка відновленого файлу не пройдена.",
        RestoreError.StateSaveFailed => "Не вдалося зберегти стан встановлення після відновлення.",
        RestoreError.RecoveryFailed => "Не вдалося повністю відкотити зміни. Перевірте файли гри та журнал.",
        RestoreError.RestorePointNotFound => "Резервну копію не знайдено.",
        RestoreError.RestorePointInvalid => "Резервна копія пошкоджена або непридатна для відновлення.",
        RestoreError.StateRestoreFailed => "Не вдалося відновити стан локалізації. Попередній стан було повернуто.",
        _ => "Невідома помилка відновлення."
    };

}

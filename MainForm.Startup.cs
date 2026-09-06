using BdoClient.Api;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

public partial class MainForm
{
    // --- Startup ---

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        _initializing = true;
        try
        {
            var configLoad = _configStore.Load();
            var config = configLoad.Value ?? new Config();

            SetOperationState(OperationState.LoadingApi);
            SetGameSearching();
            ShowModeLoadingPlaceholder();
            SetMessage("Завантаження даних з сервера...\nПерший запит може зайняти до 30 секунд. Будь ласка, зачекайте.");

            // Timer to show elapsed time during startup
            _startupStartTime = DateTime.Now;
            _startupTimer = new System.Windows.Forms.Timer();
            _startupTimer.Interval = 1000;
            _startupTimer.Tick += (s, args) =>
            {
                var elapsed = (int)(DateTime.Now - _startupStartTime).TotalSeconds;
                SetMessage($"Завантаження даних з сервера... ({elapsed} сек)\nПерший запит може зайняти до 30 секунд. Будь ласка, зачекайте.");
            };
            _startupTimer.Start();

            // Fire-and-forget warmup to pre-warm DNS/TLS cache
            _ = _apiClient.WarmupConnectionAsync();

            var coordinator = new StartupCoordinator(
                () => _apiClient.GetReleasesAsync(),
                (patterns) => _gameDetector.DetectAsync(patterns),
                _logger);

            var result = await coordinator.RunAsync(
                onLocalDetectionComplete: localResult =>
                {
                    if (localResult.GamePath != null)
                    {
                        _gameRoot = localResult.GamePath;
                        SetGameFound(localResult.GamePath, localResult.Source);
                    }
                    else
                    {
                        SetGameNotFound("Локально гру не знайдено. Очікування даних сервера...");
                    }
                },
                onApiComplete: apiResult =>
                {
                    if (apiResult.Success && apiResult.Response != null)
                    {
                        _apiResponse = apiResult.Response;
                        _apiLoadedSuccessfully = true;
                        _apiErrorMessage = null;
                        _apiErrorKind = ApiErrorKind.None;
                        BuildDynamicModes();
                        RestoreInitialMode(config);
                    }
                    else
                    {
                        _apiLoadedSuccessfully = false;
                        _apiErrorKind = apiResult.ErrorKind;
                        _apiErrorMessage = apiResult.ErrorMessage;
                        ShowModeFailurePlaceholder();
                        SetMessage(ApiErrorPresentation.GetUserMessage(apiResult.ErrorKind, apiResult.ErrorMessage));
                    }
                },
                onFallbackStarted: () =>
                {
                    SetGameNotFound("Пошук гри за даними сервера...");
                });

            if (result.FinalGamePath != null)
            {
                _gameRoot = result.FinalGamePath;
                SetGameFound(result.FinalGamePath, result.FinalGameSource);
            }
            else
            {
                SetGameNotFound("Гру не знайдено");
            }

            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup error: {ex.Message}");
            SetMessage($"Помилка запуску: {ex.Message}");
        }
        finally
        {
            _startupTimer?.Stop();
            _startupTimer?.Dispose();
            _startupTimer = null;
            _initializing = false;
            SetOperationState(OperationState.Idle);
            if (!_closing)
            {
                _poller.Start(_apiResponse);
                await StartupUpdateLifecycleCoordinator.RunAsync(
                    RunStartupLifecycleMaintenanceAsync,
                    () => _closing,
                    StartApplicationUpdateMonitoring,
                    ex => _logger.Warning($"Startup lifecycle maintenance failed: {ex.Message}"));
            }
        }
    }

    private async Task RunStartupLifecycleMaintenanceAsync()
    {
        try
        {
            await Task.Run(() => _updateLifecycle.RunStartupMaintenance());
        }
        catch (Exception ex)
        {
            _logger.Warning($"Startup lifecycle maintenance failed: {ex.Message}");
        }
    }
}

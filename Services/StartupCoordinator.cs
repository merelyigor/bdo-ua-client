using System.Diagnostics;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Services;

internal sealed class StartupGameResult
{
    public string? GamePath { get; }
    public DetectionSource? Source { get; }

    public StartupGameResult(string? gamePath, DetectionSource? source)
    {
        GamePath = gamePath;
        Source = source;
    }
}

internal sealed class StartupApiResult
{
    public bool Success { get; }
    public ReleasesResponse? Response { get; }
    public ApiErrorKind ErrorKind { get; }
    public string? ErrorMessage { get; }

    public StartupApiResult(bool success, ReleasesResponse? response, ApiErrorKind errorKind, string? errorMessage)
    {
        Success = success;
        Response = response;
        ErrorKind = errorKind;
        ErrorMessage = errorMessage;
    }
}

internal sealed class StartupCoordinatorResult
{
    public string? FinalGamePath { get; }
    public DetectionSource? FinalGameSource { get; }
    public bool ApiSuccess { get; }
    public ReleasesResponse? ApiResponse { get; }
    public ApiErrorKind ApiErrorKind { get; }
    public string? ApiErrorMessage { get; }

    public StartupCoordinatorResult(
        string? finalGamePath, DetectionSource? finalGameSource,
        bool apiSuccess, ReleasesResponse? apiResponse,
        ApiErrorKind apiErrorKind, string? apiErrorMessage)
    {
        FinalGamePath = finalGamePath;
        FinalGameSource = finalGameSource;
        ApiSuccess = apiSuccess;
        ApiResponse = apiResponse;
        ApiErrorKind = apiErrorKind;
        ApiErrorMessage = apiErrorMessage;
    }
}

internal sealed class StartupCoordinator
{
    private readonly Func<CancellationToken, Task<ApiResult<ReleasesResponse>>> _loadApi;
    private readonly Func<IReadOnlyList<InstallPathPattern>?, CancellationToken, Task<DetectionResult>> _detectGame;
    private readonly ILogger _logger;

    public StartupCoordinator(
        Func<Task<ApiResult<ReleasesResponse>>> loadApi,
        Func<IReadOnlyList<InstallPathPattern>?, Task<DetectionResult>> detectGame,
        ILogger logger)
        : this(_ => loadApi(), (patterns, _) => detectGame(patterns), logger)
    {
        ArgumentNullException.ThrowIfNull(loadApi);
        ArgumentNullException.ThrowIfNull(detectGame);
    }

    public StartupCoordinator(
        Func<CancellationToken, Task<ApiResult<ReleasesResponse>>> loadApi,
        Func<IReadOnlyList<InstallPathPattern>?, CancellationToken, Task<DetectionResult>> detectGame,
        ILogger logger)
    {
        _loadApi = loadApi;
        _detectGame = detectGame;
        _logger = logger;
    }

    public async Task<StartupCoordinatorResult> RunAsync(
        Action<StartupGameResult>? onLocalDetectionComplete = null,
        Action<StartupApiResult>? onApiComplete = null,
        Action? onFallbackStarted = null,
        CancellationToken cancellationToken = default)
    {
        var startupSw = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var apiTask = _loadApi(cancellationToken);
        var localTask = _detectGame(null, cancellationToken);

        bool localDone = false;
        bool apiDone = false;

        string? gamePath = null;
        DetectionSource? gameSource = null;
        bool apiSuccess = false;
        ReleasesResponse? apiResponse = null;
        ApiErrorKind apiErrorKind = ApiErrorKind.None;
        string? apiErrorMessage = null;

        while (!localDone || !apiDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!localDone && localTask.IsCompleted)
            {
                localDone = true;
                try
                {
                    var detection = await localTask;
                    if (detection.IsFound && detection.GamePath != null)
                    {
                        gamePath = detection.GamePath;
                        gameSource = detection.Source;
                        _logger.Info($"Startup local game detection found: {detection.GamePath} ({detection.Source})");
                        onLocalDetectionComplete?.Invoke(new StartupGameResult(detection.GamePath, detection.Source));
                    }
                    else
                    {
                        _logger.Info("Startup local game detection not found");
                        onLocalDetectionComplete?.Invoke(new StartupGameResult(null, null));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Startup local detection exception: {ex.Message}");
                    onLocalDetectionComplete?.Invoke(new StartupGameResult(null, null));
                }
            }

            if (!apiDone && apiTask.IsCompleted)
            {
                apiDone = true;
                try
                {
                    var apiResult = await apiTask;
                    if (apiResult.IsSuccess && apiResult.Value?.Data?.Modes != null)
                    {
                        apiSuccess = true;
                        apiResponse = apiResult.Value;
                        _logger.Info("Startup API loading completed");
                        onApiComplete?.Invoke(new StartupApiResult(true, apiResult.Value, ApiErrorKind.None, null));
                    }
                    else
                    {
                        apiErrorKind = apiResult.ErrorKind;
                        apiErrorMessage = apiResult.ErrorMessage ?? "Невідома помилка сервера.";
                        _logger.Warning($"Startup API failed: {apiErrorMessage}");
                        onApiComplete?.Invoke(new StartupApiResult(false, null, apiErrorKind, apiErrorMessage));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    apiErrorKind = ApiErrorKind.Unexpected;
                    apiErrorMessage = ex.Message;
                    _logger.Error($"Startup API exception: {ex.Message}");
                    onApiComplete?.Invoke(new StartupApiResult(false, null, ApiErrorKind.Unexpected, ex.Message));
                }
            }

            if (!localDone && !apiDone)
            {
                if (cancellationToken.CanBeCanceled)
                {
                    var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    var completedTask = await Task.WhenAny(apiTask, localTask, cancellationTask);
                    if (completedTask == cancellationTask)
                        cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    await Task.WhenAny(apiTask, localTask);
                }
            }
            else if (!localDone)
                await localTask;
            else if (!apiDone)
                await apiTask;
        }

        // Fallback: local NotFound + API success with patterns
        if (gamePath == null && apiSuccess)
        {
            if (apiResponse?.Data?.InstallPathPatterns is { Count: > 0 } patterns)
            {
                _logger.Info("Startup API-assisted detection started");
                onFallbackStarted?.Invoke();
                try
                {
                    var fallbackResult = await _detectGame(patterns, cancellationToken);
                    if (fallbackResult.IsFound && fallbackResult.GamePath != null)
                    {
                        gamePath = fallbackResult.GamePath;
                        gameSource = fallbackResult.Source;
                        _logger.Info($"Startup API-assisted detection found: {fallbackResult.GamePath}");
                    }
                    else
                    {
                        _logger.Info("Startup API-assisted detection not found");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Startup fallback detection exception: {ex.Message}");
                }
            }
            else
            {
                _logger.Info("Startup: API success but no install_path_patterns for fallback");
            }
        }

        startupSw.Stop();
        _logger.Info($"Startup completed in {startupSw.ElapsedMilliseconds}ms (gamePath={gamePath != null}, apiSuccess={apiSuccess})");

        return new StartupCoordinatorResult(
            gamePath, gameSource,
            apiSuccess, apiResponse,
            apiErrorKind, apiErrorMessage);
    }
}

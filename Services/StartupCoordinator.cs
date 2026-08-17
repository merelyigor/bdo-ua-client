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

internal sealed class StartupCoordinator
{
    private readonly Func<Task<ApiResult<ReleasesResponse>>> _loadApi;
    private readonly Func<IReadOnlyList<InstallPathPattern>?, Task<DetectionResult>> _detectGame;
    private readonly ILogger _logger;

    public StartupCoordinator(
        Func<Task<ApiResult<ReleasesResponse>>> loadApi,
        Func<IReadOnlyList<InstallPathPattern>?, Task<DetectionResult>> detectGame,
        ILogger logger)
    {
        _loadApi = loadApi;
        _detectGame = detectGame;
        _logger = logger;
    }

    public async Task RunAsync(
        Action<StartupGameResult> onLocalDetectionComplete,
        Action<StartupApiResult> onApiComplete,
        Action<StartupGameResult> onFallbackDetectionComplete,
        CancellationToken cancellationToken = default)
    {
        var apiTask = _loadApi();
        var localTask = _detectGame(null);

        bool localDone = false;
        bool apiDone = false;
        bool localFound = false;

        while (!localDone || !apiDone)
        {
            if (!localDone && localTask.IsCompleted)
            {
                localDone = true;
                var detection = await localTask;
                if (detection.IsFound && detection.GamePath != null)
                {
                    localFound = true;
                    _logger.Info($"Startup local game detection found: {detection.GamePath} ({detection.Source})");
                    onLocalDetectionComplete(new StartupGameResult(detection.GamePath, detection.Source));
                }
                else
                {
                    _logger.Info("Startup local game detection not found");
                    onLocalDetectionComplete(new StartupGameResult(null, null));
                }
            }

            if (!apiDone && apiTask.IsCompleted)
            {
                apiDone = true;
                var apiResult = await apiTask;
                if (apiResult.IsSuccess && apiResult.Value?.Data?.Modes != null)
                {
                    _logger.Info("Startup API loading completed");
                    onApiComplete(new StartupApiResult(true, apiResult.Value, ApiErrorKind.None, null));
                }
                else
                {
                    var kind = apiResult.ErrorKind;
                    var msg = apiResult.ErrorMessage ?? "Невідома помилка сервера.";
                    _logger.Warning($"Startup API failed: {msg}");
                    onApiComplete(new StartupApiResult(false, null, kind, msg));
                }
            }

            if (!localDone && !apiDone)
                await Task.WhenAny(apiTask, localTask);
            else if (!localDone)
                await localTask;
            else if (!apiDone)
                await apiTask;
        }

        // Fallback: local NotFound + API success with patterns
        if (!localFound && apiDone)
        {
            // Re-read API result (already awaited)
            var apiResult = await apiTask;
            if (apiResult.IsSuccess && apiResult.Value?.Data?.InstallPathPatterns is { Count: > 0 } patterns)
            {
                _logger.Info("Startup API-assisted detection started");
                var fallbackResult = await _detectGame(patterns);
                if (fallbackResult.IsFound && fallbackResult.GamePath != null)
                {
                    _logger.Info($"Startup API-assisted detection found: {fallbackResult.GamePath}");
                    onFallbackDetectionComplete(new StartupGameResult(fallbackResult.GamePath, fallbackResult.Source));
                }
                else
                {
                    _logger.Info("Startup API-assisted detection not found");
                    onFallbackDetectionComplete(new StartupGameResult(null, null));
                }
            }
        }
    }
}

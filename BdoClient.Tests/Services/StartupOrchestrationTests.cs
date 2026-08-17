using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class StartupOrchestrationTests
{
    private static readonly InstallPathPattern TestPattern = new()
    {
        Pattern = @"{drive}:\Games\Black Desert Online\ads\",
        Launcher = "steam",
        Description = "Steam default"
    };

    private static ReleasesResponse MakeSuccessResponse(List<InstallPathPattern>? patterns = null)
    {
        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                Modes = new List<LocalizationMode>(),
                InstallPathPatterns = patterns
            }
        };
    }

    [Fact]
    public async Task LocalFirst_GameFound_BeforeApiCompletes()
    {
        var apiTcs = new TaskCompletionSource<ApiResult<ReleasesResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackOrder = 0;
        int localOrder = -1;
        int apiOrder = -1;
        string? localPath = null;

        var coordinator = new StartupCoordinator(
            loadApi: () => apiTcs.Task,
            detectGame: _ => Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.SavedConfig)),
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onLocalDetectionComplete: r =>
            {
                localPath = r.GamePath;
                localOrder = Interlocked.Increment(ref callbackOrder);
            },
            onApiComplete: r => apiOrder = Interlocked.Increment(ref callbackOrder),
            onFallbackDetectionComplete: _ => throw new Exception("fallback should not run"));

        // Complete API on background thread to avoid tight-loop starvation
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            apiTcs.SetResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse()));
        });

        await runTask;

        Assert.Equal(@"C:\Games\BDO", localPath);
        Assert.Equal(1, localOrder);
        Assert.Equal(2, apiOrder);
    }

    [Fact]
    public async Task ApiFirst_ApiSuccess_BeforeLocalCompletes()
    {
        var localTcs = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackOrder = 0;
        int localOrder = -1;
        int apiOrder = -1;
        bool apiSuccess = false;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse())),
            detectGame: _ => localTcs.Task,
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onLocalDetectionComplete: r => localOrder = Interlocked.Increment(ref callbackOrder),
            onApiComplete: r =>
            {
                apiSuccess = r.Success;
                apiOrder = Interlocked.Increment(ref callbackOrder);
            },
            onFallbackDetectionComplete: _ => throw new Exception("fallback should not run"));

        // Complete local on background thread to avoid tight-loop starvation
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            localTcs.SetResult(DetectionResult.Found(@"D:\BDO", DetectionSource.Registry));
        });

        await runTask;

        Assert.True(apiSuccess);
        Assert.Equal(1, apiOrder);
        Assert.Equal(2, localOrder);
    }

    [Fact]
    public async Task LocalNotFound_ApiSuccessWithPatterns_FallbackRuns()
    {
        var callLog = new List<IReadOnlyList<InstallPathPattern>?>();

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: patterns =>
            {
                callLog.Add(patterns);
                return Task.FromResult(patterns is { Count: > 0 }
                    ? DetectionResult.Found(@"E:\Games\BDO", DetectionSource.ApiPattern)
                    : DetectionResult.NotFound());
            },
            logger: new TestLogger());

        string? fallbackPath = null;

        await coordinator.RunAsync(
            onLocalDetectionComplete: _ => { },
            onApiComplete: _ => { },
            onFallbackDetectionComplete: r => fallbackPath = r.GamePath);

        Assert.Equal(@"E:\Games\BDO", fallbackPath);
        Assert.Equal(2, callLog.Count);
        Assert.Null(callLog[0]);
        Assert.NotNull(callLog[1]);
        Assert.Single(callLog[1]!);
    }

    [Fact]
    public async Task ApiFirst_LocalNotFound_FallbackRuns()
    {
        var localTcs = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? fallbackPath = null;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: patterns => patterns is { Count: > 0 }
                ? Task.FromResult(DetectionResult.Found(@"F:\BDO", DetectionSource.ApiPattern))
                : localTcs.Task,
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onLocalDetectionComplete: _ => { },
            onApiComplete: _ => { },
            onFallbackDetectionComplete: r => fallbackPath = r.GamePath);

        // Local still pending — fallback should not have run yet
        Assert.Null(fallbackPath);

        // Complete local as NotFound on background thread to avoid tight-loop starvation
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            localTcs.SetResult(DetectionResult.NotFound());
        });

        await runTask;

        Assert.Equal(@"F:\BDO", fallbackPath);
    }

    [Fact]
    public async Task LocalFound_ApiSuccess_NoFallback()
    {
        int detectCallCount = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: _ =>
            {
                Interlocked.Increment(ref detectCallCount);
                return Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.Steam));
            },
            logger: new TestLogger());

        await coordinator.RunAsync(
            onLocalDetectionComplete: _ => { },
            onApiComplete: _ => { },
            onFallbackDetectionComplete: _ => throw new Exception("fallback should not run"));

        Assert.Equal(1, detectCallCount);
    }

    [Fact]
    public async Task LocalNotFound_ApiFailure_NoFallback()
    {
        bool fallbackCalled = false;
        bool apiSuccess = true;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, "timeout")),
            detectGame: _ => Task.FromResult(DetectionResult.NotFound()),
            logger: new TestLogger());

        await coordinator.RunAsync(
            onLocalDetectionComplete: _ => { },
            onApiComplete: r => apiSuccess = r.Success,
            onFallbackDetectionComplete: _ => fallbackCalled = true);

        Assert.False(fallbackCalled);
        Assert.False(apiSuccess);
    }

    [Fact]
    public async Task LocalNotFound_ApiSuccessNoPatterns_FinalNotFound()
    {
        bool fallbackCalled = false;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(patterns: null))),
            detectGame: _ => Task.FromResult(DetectionResult.NotFound()),
            logger: new TestLogger());

        await coordinator.RunAsync(
            onLocalDetectionComplete: _ => { },
            onApiComplete: _ => { },
            onFallbackDetectionComplete: _ => fallbackCalled = true);

        Assert.False(fallbackCalled);
    }

    [Fact]
    public async Task ApiFailure_PreservesLocalFound()
    {
        string? localPath = null;
        bool apiSuccess = true;
        bool fallbackCalled = false;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, "dns error")),
            detectGame: _ => Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.SavedConfig)),
            logger: new TestLogger());

        await coordinator.RunAsync(
            onLocalDetectionComplete: r => localPath = r.GamePath,
            onApiComplete: r => apiSuccess = r.Success,
            onFallbackDetectionComplete: _ => fallbackCalled = true);

        Assert.Equal(@"C:\Games\BDO", localPath);
        Assert.False(apiSuccess);
        Assert.False(fallbackCalled);
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

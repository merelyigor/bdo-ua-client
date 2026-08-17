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
    public async Task LocalFirst_GameFound_ApiSuccess_FinalFound()
    {
        var apiTcs = new TaskCompletionSource<ApiResult<ReleasesResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int localCb = 0;
        int apiCb = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => apiTcs.Task,
            detectGame: _ => Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.SavedConfig)),
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onLocalDetectionComplete: r =>
            {
                Interlocked.Increment(ref localCb);
                apiTcs.TrySetResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse()));
            },
            onApiComplete: _ => Interlocked.Increment(ref apiCb));

        var result = await runTask;

        Assert.Equal(@"C:\Games\BDO", result.FinalGamePath);
        Assert.Equal(DetectionSource.SavedConfig, result.FinalGameSource);
        Assert.True(result.ApiSuccess);
        Assert.Equal(1, localCb);
        Assert.Equal(1, apiCb);
    }

    [Fact]
    public async Task ApiFirst_ApiSuccess_LocalFound_FinalFound()
    {
        var localTcs = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int localCb = 0;
        int apiCb = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse())),
            detectGame: _ => localTcs.Task,
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onLocalDetectionComplete: r => Interlocked.Increment(ref localCb),
            onApiComplete: r =>
            {
                Interlocked.Increment(ref apiCb);
                localTcs.TrySetResult(DetectionResult.Found(@"D:\BDO", DetectionSource.Registry));
            });

        var result = await runTask;

        Assert.Equal(@"D:\BDO", result.FinalGamePath);
        Assert.Equal(DetectionSource.Registry, result.FinalGameSource);
        Assert.True(result.ApiSuccess);
        Assert.Equal(1, localCb);
        Assert.Equal(1, apiCb);
    }

    [Fact]
    public async Task LocalNotFound_ApiFailure_FinalNotFound()
    {
        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, "timeout")),
            detectGame: _ => Task.FromResult(DetectionResult.NotFound()),
            logger: new TestLogger());

        var result = await coordinator.RunAsync();

        Assert.Null(result.FinalGamePath);
        Assert.False(result.ApiSuccess);
        Assert.Equal(ApiErrorKind.Timeout, result.ApiErrorKind);
    }

    [Fact]
    public async Task LocalNotFound_ApiSuccessNoPatterns_FinalNotFound()
    {
        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(patterns: null))),
            detectGame: _ => Task.FromResult(DetectionResult.NotFound()),
            logger: new TestLogger());

        var result = await coordinator.RunAsync();

        Assert.Null(result.FinalGamePath);
        Assert.True(result.ApiSuccess);
    }

    [Fact]
    public async Task LocalNotFound_ApiSuccessWithPatterns_FallbackFound()
    {
        int detectCalls = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: patterns =>
            {
                Interlocked.Increment(ref detectCalls);
                return Task.FromResult(patterns is { Count: > 0 }
                    ? DetectionResult.Found(@"E:\Games\BDO", DetectionSource.ApiPattern)
                    : DetectionResult.NotFound());
            },
            logger: new TestLogger());

        var result = await coordinator.RunAsync();

        Assert.Equal(@"E:\Games\BDO", result.FinalGamePath);
        Assert.Equal(DetectionSource.ApiPattern, result.FinalGameSource);
        Assert.True(result.ApiSuccess);
        Assert.Equal(2, detectCalls);
    }

    [Fact]
    public async Task LocalNotFound_ApiSuccessWithPatterns_FallbackNotFound()
    {
        int detectCalls = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: _ =>
            {
                Interlocked.Increment(ref detectCalls);
                return Task.FromResult(DetectionResult.NotFound());
            },
            logger: new TestLogger());

        var result = await coordinator.RunAsync();

        Assert.Null(result.FinalGamePath);
        Assert.True(result.ApiSuccess);
        Assert.Equal(2, detectCalls);
    }

    [Fact]
    public async Task ApiFirst_NoPatterns_LocalNotFound_FinalNotFound()
    {
        var localTcs = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(patterns: null))),
            detectGame: _ => localTcs.Task,
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onApiComplete: _ => localTcs.TrySetResult(DetectionResult.NotFound()));

        var result = await runTask;

        Assert.Null(result.FinalGamePath);
        Assert.True(result.ApiSuccess);
    }

    [Fact]
    public async Task LocalFirst_Fallback_Deterministic()
    {
        var apiTcs = new TaskCompletionSource<ApiResult<ReleasesResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int fallbackCb = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => apiTcs.Task,
            detectGame: patterns => Task.FromResult(patterns is { Count: > 0 }
                ? DetectionResult.Found(@"F:\BDO", DetectionSource.ApiPattern)
                : DetectionResult.NotFound()),
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onLocalDetectionComplete: r =>
            {
                apiTcs.TrySetResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern })));
            },
            onFallbackStarted: () => Interlocked.Increment(ref fallbackCb));

        var result = await runTask;

        Assert.Equal(@"F:\BDO", result.FinalGamePath);
        Assert.Equal(1, fallbackCb);
    }

    [Fact]
    public async Task ApiFirst_Fallback_Deterministic()
    {
        var localTcs = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int fallbackCb = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: patterns => patterns is { Count: > 0 }
                ? Task.FromResult(DetectionResult.Found(@"G:\BDO", DetectionSource.ApiPattern))
                : localTcs.Task,
            logger: new TestLogger());

        var runTask = coordinator.RunAsync(
            onApiComplete: r =>
            {
                localTcs.TrySetResult(DetectionResult.NotFound());
            },
            onFallbackStarted: () => Interlocked.Increment(ref fallbackCb));

        var result = await runTask;

        Assert.Equal(@"G:\BDO", result.FinalGamePath);
        Assert.Equal(1, fallbackCb);
    }

    [Fact]
    public async Task LocalFound_ApiSuccess_NoFallback()
    {
        int detectCalls = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: _ =>
            {
                Interlocked.Increment(ref detectCalls);
                return Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.Steam));
            },
            logger: new TestLogger());

        var result = await coordinator.RunAsync();

        Assert.Equal(@"C:\Games\BDO", result.FinalGamePath);
        Assert.Equal(1, detectCalls);
    }

    [Fact]
    public async Task ApiFailure_PreservesLocalFound()
    {
        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, "dns error")),
            detectGame: _ => Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.SavedConfig)),
            logger: new TestLogger());

        var result = await coordinator.RunAsync();

        Assert.Equal(@"C:\Games\BDO", result.FinalGamePath);
        Assert.False(result.ApiSuccess);
        Assert.Equal(ApiErrorKind.Network, result.ApiErrorKind);
    }

    [Fact]
    public async Task CallbackException_Propagates_OtherTaskObserved()
    {
        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse())),
            detectGame: _ => Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.SavedConfig)),
            logger: new TestLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunAsync(
                onLocalDetectionComplete: _ => throw new InvalidOperationException("test")));
    }

    [Fact]
    public async Task FallbackStarted_NotCalled_WhenLocalFound()
    {
        int fallbackStarted = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Success(MakeSuccessResponse(new List<InstallPathPattern> { TestPattern }))),
            detectGame: _ => Task.FromResult(DetectionResult.Found(@"C:\Games\BDO", DetectionSource.Steam)),
            logger: new TestLogger());

        await coordinator.RunAsync(
            onFallbackStarted: () => Interlocked.Increment(ref fallbackStarted));

        Assert.Equal(0, fallbackStarted);
    }

    [Fact]
    public async Task FallbackStarted_NotCalled_WhenApiFailure()
    {
        int fallbackStarted = 0;

        var coordinator = new StartupCoordinator(
            loadApi: () => Task.FromResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, "timeout")),
            detectGame: _ => Task.FromResult(DetectionResult.NotFound()),
            logger: new TestLogger());

        await coordinator.RunAsync(
            onFallbackStarted: () => Interlocked.Increment(ref fallbackStarted));

        Assert.Equal(0, fallbackStarted);
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

using BdoClient.Api;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class StartupOrchestrationTests
{
    [Fact]
    public async Task LocalDetection_CompletesBeforeApi_GameFoundBeforeApiCompletes()
    {
        var apiTcs = new TaskCompletionSource<ApiResult<ReleasesResponse>>();
        var localDetectionResult = DetectionResult.Found(@"C:\Games\BlackDesert", DetectionSource.SavedConfig);

        bool localDetectionCompleted = false;
        bool apiCompleted = false;
        string? gamePath = null;

        var apiTask = apiTcs.Task;
        var localTask = Task.FromResult(localDetectionResult);

        var detection = await localTask;
        localDetectionCompleted = true;
        if (detection.IsFound)
            gamePath = detection.GamePath;

        Assert.True(localDetectionCompleted);
        Assert.Equal(@"C:\Games\BlackDesert", gamePath);
        Assert.False(apiCompleted);

        apiTcs.SetResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, "timeout"));
        var apiResult = await apiTask;
        apiCompleted = true;

        Assert.True(apiCompleted);
        Assert.False(apiResult.IsSuccess);
    }

    [Fact]
    public void ApiFailure_DoesNotEraseLocalDetection()
    {
        var localDetection = DetectionResult.Found(@"C:\Games\BlackDesert", DetectionSource.Steam);
        var apiResult = ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, "connection failed");

        string? gameRoot = null;
        bool apiLoaded = false;

        if (localDetection.IsFound)
            gameRoot = localDetection.GamePath;

        if (apiResult.IsSuccess)
            apiLoaded = true;

        Assert.NotNull(gameRoot);
        Assert.Equal(@"C:\Games\BlackDesert", gameRoot);
        Assert.False(apiLoaded);
    }

    [Fact]
    public void LocalNotFound_ApiSuccess_NoGamePathYet()
    {
        var localDetection = DetectionResult.NotFound();

        string? gameRoot = null;
        if (localDetection.IsFound)
            gameRoot = localDetection.GamePath;

        Assert.Null(gameRoot);
    }

    [Fact]
    public async Task LocalDetection_CompletesWhileApiStillPending_GamePathAvailable()
    {
        var apiTcs = new TaskCompletionSource<ApiResult<ReleasesResponse>>();
        var apiTask = apiTcs.Task;
        var localDetectionResult = DetectionResult.Found(@"D:\BDO\Black Desert Online", DetectionSource.Registry);

        string? gamePath = null;
        bool apiStillPending = true;

        var localTask = Task.FromResult(localDetectionResult);
        var detection = await localTask;

        if (detection.IsFound)
            gamePath = detection.GamePath;

        apiStillPending = !apiTask.IsCompleted;

        Assert.Equal(@"D:\BDO\Black Desert Online", gamePath);
        Assert.True(apiStillPending);

        apiTcs.SetResult(ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, "dns error"));
        await apiTask;
    }
}

using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace BdoClient.Tests.Services;

public class ReleaseFeedPollerTests
{
    [Fact]
    public async Task UnchangedFeed_NoNotification()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new StubHttpHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        var notifications = new List<ReleasesResponse>();
        poller.OnFeedCandidate += f => notifications.Add(f);

        poller.Start(feed);

        await Task.Delay(1000);
        poller.Stop();
        await Task.Delay(200);

        Assert.Empty(notifications);
        poller.Dispose();
    }

    [Fact]
    public async Task NewModeAdded_NotifiesWithNewFeed()
    {
        var oldFeed = CreateFeed(modeA: "id1");
        var newFeed = CreateFeed(modeA: "id1", modeB: "id2");
        var handler = new StubHttpHandler(newFeed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        var notifications = new List<ReleasesResponse>();
        poller.OnFeedCandidate += f => { notifications.Add(f); poller.AcceptFeed(f); };

        poller.Start(oldFeed);

        await Task.Delay(1000);
        poller.Stop();
        await Task.Delay(200);

        Assert.NotEmpty(notifications);
        Assert.NotNull(notifications[0].Data?.Modes);
        Assert.Equal(2, notifications[0].Data!.Modes!.Count);
        poller.Dispose();
    }

    [Fact]
    public async Task NewerPublicId_Notifies()
    {
        var oldFeed = CreateFeedWithVersion(modeA: "id1", version: 1);
        var newFeed = CreateFeedWithVersion(modeA: "id1-updated", version: 2);
        var handler = new StubHttpHandler(newFeed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        var notifications = new List<ReleasesResponse>();
        poller.OnFeedCandidate += f => { notifications.Add(f); poller.AcceptFeed(f); };

        poller.Start(oldFeed);

        await Task.Delay(1000);
        poller.Stop();
        await Task.Delay(200);

        Assert.NotEmpty(notifications);
        poller.Dispose();
    }

    [Fact]
    public async Task PollFailure_KeepsOldFeed()
    {
        var oldFeed = CreateFeed(modeA: "id1");
        var handler = new FailingHttpHandler();
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        var notifications = new List<ReleasesResponse>();
        poller.OnFeedCandidate += f => notifications.Add(f);
        var failures = new List<string>();
        poller.OnPollFailed += e => failures.Add(e);

        poller.Start(oldFeed);

        await Task.Delay(1000);
        poller.Stop();
        await Task.Delay(200);

        Assert.Empty(notifications);
        Assert.NotEmpty(failures);
        poller.Dispose();
    }

    [Fact]
    public async Task Shutdown_StopsPoller()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new StubHttpHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        poller.Start(feed);

        Assert.True(poller.IsRunning);

        poller.Stop();
        await Task.Delay(300);

        Assert.False(poller.IsRunning);
        poller.Dispose();
    }

    [Fact]
    public async Task SecondStart_DoesNotCreateOverlappingLoop()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new StubHttpHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        poller.Start(feed);
        poller.Start(feed);

        await Task.Delay(500);
        poller.Stop();
        await Task.Delay(200);

        poller.Dispose();
    }

    [Fact]
    public async Task NotAcceptedFeed_StillReportedAsChanged()
    {
        var oldFeed = CreateFeed(modeA: "id1");
        var newFeed = CreateFeed(modeA: "id1", modeB: "id2");
        var handler = new StubHttpHandler(newFeed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        var count = 0;
        poller.OnFeedCandidate += _ => Interlocked.Increment(ref count);

        poller.Start(oldFeed);

        await Task.Delay(1000);
        poller.Stop();
        await Task.Delay(200);

        Assert.True(count >= 2, $"Expected multiple notifications for not-accepted feed, got {count}");
        poller.Dispose();
    }

    [Fact]
    public async Task AcceptedFeed_StopsReportingChanged()
    {
        var oldFeed = CreateFeed(modeA: "id1");
        var newFeed = CreateFeed(modeA: "id1", modeB: "id2");
        var handler = new StubHttpHandler(newFeed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        var count = 0;
        poller.OnFeedCandidate += f =>
        {
            if (count == 0)
            {
                poller.AcceptFeed(f);
            }
            Interlocked.Increment(ref count);
        };

        poller.Start(oldFeed);

        await Task.Delay(1500);
        poller.Stop();
        await Task.Delay(200);

        Assert.Equal(1, count);
        poller.Dispose();
    }

    [Fact]
    public async Task MaxConcurrency_OneSimultaneousRequest()
    {
        var oldFeed = CreateFeed(modeA: "id1");
        var newFeed = CreateFeed(modeA: "id1", modeB: "id2");
        var handler = new ConcurrencyTrackingHandler(newFeed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var interval = TimeSpan.FromMilliseconds(100);

        var poller = new ReleaseFeedPoller(apiClient, logger, interval);
        poller.OnFeedCandidate += f => poller.AcceptFeed(f);

        poller.Start(oldFeed);
        await Task.Delay(1500);
        poller.Stop();
        await Task.Delay(200);

        Assert.True(handler.MaxConcurrency <= 1, $"Max concurrency was {handler.MaxConcurrency}");
        poller.Dispose();
    }

    [Fact]
    public void DisposedPoller_DoesNotStart()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new StubHttpHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);

        var poller = new ReleaseFeedPoller(apiClient, logger);
        poller.Dispose();

        poller.Start(feed);
        Assert.False(poller.IsRunning);
    }

    [Fact]
    public void Pause_Resume_ToggleState()
    {
        var logger = new RecordingLogger();
        var handler = new StubHttpHandler(CreateFeed(modeA: "id1"));
        var httpClient = new HttpClient(handler);
        var apiClient = new BdoUaApiClient(httpClient, logger);

        var poller = new ReleaseFeedPoller(apiClient, logger);
        poller.Pause();
        poller.Resume();
        poller.Dispose();
    }

    private class ConcurrencyTrackingHandler : HttpMessageHandler
    {
        private readonly ReleasesResponse _response;
        private int _activeCount;
        public int MaxConcurrency { get; private set; }

        public ConcurrencyTrackingHandler(ReleasesResponse response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _activeCount);
            lock (this) { if (current > MaxConcurrency) MaxConcurrency = current; }
            try
            {
                await Task.Delay(50, cancellationToken);
                var json = System.Text.Json.JsonSerializer.Serialize(_response);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }
    }

    private static ReleasesResponse CreateFeed(
        string? modeA = null, string? modeB = null)
    {
        var modes = new List<LocalizationMode>();
        if (modeA != null)
            modes.Add(CreateMode("full-ukrainian", modeA));
        if (modeB != null)
            modes.Add(CreateMode("english-items", modeB));

        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                OfficialPatch = 100,
                Modes = modes
            }
        };
    }

    private static ReleasesResponse CreateFeedWithVersion(string modeA, int version)
    {
        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                OfficialPatch = 100,
                Modes = new List<LocalizationMode>
                {
                    new LocalizationMode
                    {
                        Slug = "full-ukrainian",
                        PublicName = "Full",
                        Current = new CurrentRelease
                        {
                            PublicId = modeA,
                            Version = version,
                            Patch = 100,
                            CompatibleWithOfficialPatch = true,
                            DownloadUrl = "https://example.com/download",
                            SizeBytes = 1024,
                            Sha256 = "abc123"
                        }
                    }
                }
            }
        };
    }

    private static LocalizationMode CreateMode(string slug, string publicId)
    {
        return new LocalizationMode
        {
            Slug = slug,
            PublicName = slug,
            Current = new CurrentRelease
            {
                PublicId = publicId,
                Version = 1,
                Patch = 100,
                CompatibleWithOfficialPatch = true,
                DownloadUrl = "https://example.com/download",
                SizeBytes = 1024,
                Sha256 = "abc123"
            }
        };
    }

    private class StubHttpHandler : HttpMessageHandler
    {
        private readonly ReleasesResponse _response;
        public StubHttpHandler(ReleasesResponse response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_response);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private class FailingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated failure");
        }
    }

    private class RecordingLogger : ILogger
    {
        public List<string> DebugLines { get; } = new();
        public List<string> InfoLines { get; } = new();
        public List<string> WarningLines { get; } = new();
        public List<string> ErrorLines { get; } = new();

        public void Debug(string message) => DebugLines.Add(message);
        public void Info(string message) => InfoLines.Add(message);
        public void Warning(string message) => WarningLines.Add(message);
        public void Error(string message) => ErrorLines.Add(message);
    }

    // ---- T3 scheduling tests ----

    [Fact]
    public async Task BackgroundModeSelectedBeforeStart_UsesBackgroundInterval()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(2);
        var background = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible, background);
        poller.SetPollingMode(ReleaseFeedPollingMode.Background);
        var startSw = Stopwatch.StartNew();
        poller.Start(feed);

        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1500)));
        Assert.True(startSw.Elapsed < visible, $"elapsed={startSw.Elapsed}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task ModeChange_ResetsCurrentDelayToNewInterval()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(5);
        var background = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible, background);
        var startSw = Stopwatch.StartNew();
        poller.Start(feed);
        poller.SetPollingMode(ReleaseFeedPollingMode.Background);

        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1500)));
        Assert.True(startSw.Elapsed < visible, $"elapsed={startSw.Elapsed}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task ModeChange_DoesNotImmediatelyPoll()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(2);
        var background = TimeSpan.FromMilliseconds(1500);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible, background);
        var startSw = Stopwatch.StartNew();
        poller.Start(feed);
        poller.SetPollingMode(ReleaseFeedPollingMode.Background);

        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1900)));
        Assert.True(startSw.Elapsed > TimeSpan.FromMilliseconds(300), $"too early: {startSw.Elapsed}");
        Assert.True(startSw.Elapsed < visible, $"used visible: {startSw.Elapsed}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task RequestImmediatePoll_WakesLongDelay()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(5);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        var startSw = Stopwatch.StartNew();
        poller.Start(feed);
        await Task.Delay(100);
        poller.RequestImmediatePoll();

        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1500)));
        Assert.True(startSw.Elapsed < visible, $"elapsed={startSw.Elapsed}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task ImmediatePoll_ResetsCadenceNoDuplicateFromOldTimer()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(2);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        poller.Start(feed);
        await Task.Delay(100);
        poller.RequestImmediatePoll();

        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1000)));
        await Task.Delay(1400);
        Assert.Equal(1, handler.Count);

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task MultipleImmediateRequests_CoalesceToOne()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(5);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        poller.Start(feed);
        await Task.Delay(100);
        poller.RequestImmediatePoll();
        poller.RequestImmediatePoll();
        poller.RequestImmediatePoll();

        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1000)));
        await Task.Delay(800);
        Assert.Equal(1, handler.Count);

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task ImmediateDuringInFlight_NoFollowUpRequest()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new BlockingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(5);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        poller.Start(feed);
        poller.RequestImmediatePoll();

        Assert.True(handler.WaitStarted(TimeSpan.FromMilliseconds(1000)));
        poller.RequestImmediatePoll();
        poller.RequestImmediatePoll();
        await Task.Delay(200);
        Assert.Equal(0, handler.Count);
        Assert.Equal(1, handler.MaxConcurrency);

        handler.Release();
        await Task.Delay(300);
        Assert.Equal(1, handler.Count);
        Assert.True(handler.MaxConcurrency <= 1, $"Max concurrency {handler.MaxConcurrency}");

        await Task.Delay(500);
        Assert.Equal(1, handler.Count);

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task PauseDuringDelay_SuppressesScheduledPoll()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromMilliseconds(500);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        poller.Start(feed);
        poller.Pause();

        await Task.Delay(1200);
        Assert.Equal(0, handler.Count);

        poller.Resume();
        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task ImmediateWhilePaused_DroppedAndNoResumePoll()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(2);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        poller.Start(feed);
        poller.Pause();
        poller.RequestImmediatePoll();

        await Task.Delay(500);
        Assert.Equal(0, handler.Count);

        poller.Resume();
        Assert.False(handler.WaitForFirst(TimeSpan.FromMilliseconds(800)));

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task Resume_StartsFreshCurrentCadenceNoImmediate()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(2);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible);
        poller.Start(feed);
        poller.Pause();
        await Task.Delay(500);

        var resumeSw = Stopwatch.StartNew();
        poller.Resume();
        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(2500)));
        Assert.True(resumeSw.Elapsed > TimeSpan.FromMilliseconds(300), $"immediate after resume: {resumeSw.Elapsed}");
        Assert.True(resumeSw.Elapsed < visible + TimeSpan.FromMilliseconds(500), $"elapsed={resumeSw.Elapsed}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task ModeChangeWhilePaused_RememberedAfterResume()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var visible = TimeSpan.FromSeconds(2);
        var background = TimeSpan.FromMilliseconds(200);

        var poller = new ReleaseFeedPoller(apiClient, logger, visible, background);
        poller.Start(feed);
        poller.Pause();
        poller.SetPollingMode(ReleaseFeedPollingMode.Background);

        var resumeSw = Stopwatch.StartNew();
        poller.Resume();
        Assert.True(handler.WaitForFirst(TimeSpan.FromMilliseconds(1500)));
        Assert.True(resumeSw.Elapsed < visible, $"visible used: {resumeSw.Elapsed}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task Stop_WakesLongWaitPromptly()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new StubHttpHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);

        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromSeconds(30));
        poller.Start(feed);
        await Task.Delay(100);
        poller.Stop();

        var stopped = SpinWait.SpinUntil(() => !poller.IsRunning, TimeSpan.FromMilliseconds(2000));
        Assert.True(stopped);

        poller.Dispose();
    }

    [Fact]
    public async Task DisposeWhileWaiting_NoActivityAfter()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);

        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromSeconds(30));
        poller.Start(feed);
        await Task.Delay(100);
        poller.RequestImmediatePoll();
        poller.Dispose();

        var stopped = SpinWait.SpinUntil(() => !poller.IsRunning, TimeSpan.FromMilliseconds(2000));
        Assert.True(stopped);

        await Task.Delay(300);
        var countAfterDispose = handler.Count;
        poller.RequestImmediatePoll();
        poller.SetPollingMode(ReleaseFeedPollingMode.Background);
        poller.Pause();
        poller.Resume();
        await Task.Delay(300);

        Assert.Equal(countAfterDispose, handler.Count);
    }

    // ---- T3 scheduler-wait ownership tests ----

    private static CancellationTokenSource? GetSchedulerWaitCts(ReleaseFeedPoller poller)
    {
        var field = typeof(ReleaseFeedPoller).GetField(
            "_schedulerWaitCts",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (CancellationTokenSource?)field!.GetValue(poller);
    }

    [Fact]
    public async Task SchedulerWaitOwnership_OrdinaryDelay_WaitCtsNonNull()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromSeconds(2));

        poller.Start(feed);
        await Task.Delay(300);
        Assert.NotNull(GetSchedulerWaitCts(poller));

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task SchedulerWaitOwnership_ImmediateInFlight_NullThenFreshDelay()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new BlockingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromSeconds(2));

        poller.Start(feed);
        poller.RequestImmediatePoll();
        Assert.True(handler.WaitStarted(TimeSpan.FromMilliseconds(1000)));
        Assert.Null(GetSchedulerWaitCts(poller));

        handler.Release();
        await Task.Delay(300);
        Assert.NotNull(GetSchedulerWaitCts(poller));

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task SchedulerWaitOwnership_ManyWakeCycles_NoDeadlockOrOverlap()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100));

        poller.Start(feed);
        for (int i = 0; i < 30; i++)
        {
            if (i % 2 == 0)
                poller.SetPollingMode(i % 4 == 0 ? ReleaseFeedPollingMode.Background : ReleaseFeedPollingMode.Visible);
            if (i % 3 == 0)
                poller.RequestImmediatePoll();
            if (i % 5 == 0)
            {
                poller.Pause();
                poller.Resume();
            }

            await Task.Delay(30);
        }

        await Task.Delay(500);
        Assert.True(handler.Count > 0, "poller should keep polling (no deadlock)");
        Assert.True(handler.MaxConcurrency <= 1, $"network overlap detected: {handler.MaxConcurrency}");

        poller.Stop();
        await Task.Delay(200);
        poller.Dispose();
    }

    [Fact]
    public async Task SchedulerWaitOwnership_Dispose_ReleasesWaitCts()
    {
        var feed = CreateFeed(modeA: "id1");
        var handler = new CountingHandler(feed);
        var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger();
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromSeconds(2));

        poller.Start(feed);
        await Task.Delay(300);
        Assert.NotNull(GetSchedulerWaitCts(poller));

        poller.Dispose();
        await Task.Delay(200);
        Assert.Null(GetSchedulerWaitCts(poller));

        poller.RequestImmediatePoll();
        poller.SetPollingMode(ReleaseFeedPollingMode.Background);
        poller.Pause();
        poller.Resume();
        await Task.Delay(200);
    }

    private class CountingHandler : HttpMessageHandler
    {
        private readonly ReleasesResponse _response;
        private int _count;
        private int _active;
        private readonly ManualResetEventSlim _firstRequested = new(false);
        private readonly Stopwatch _firstStopwatch = new();

        public int Count => _count;
        public int MaxConcurrency { get; private set; }
        public TimeSpan FirstRequestElapsed => _firstStopwatch.Elapsed;

        public CountingHandler(ReleasesResponse response) => _response = response;

        public bool WaitForFirst(TimeSpan timeout) => _firstRequested.Wait(timeout);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_firstStopwatch.IsRunning)
                _firstStopwatch.Start();

            var current = Interlocked.Increment(ref _active);
            lock (this) { if (current > MaxConcurrency) MaxConcurrency = current; }

            Interlocked.Increment(ref _count);
            _firstRequested.Set();

            await Task.Delay(1, cancellationToken);
            var json = JsonSerializer.Serialize(_response);
            Interlocked.Decrement(ref _active);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private class BlockingHandler : HttpMessageHandler
    {
        private readonly ReleasesResponse _response;
        private readonly ManualResetEventSlim _release = new(false);
        private readonly ManualResetEventSlim _started = new(false);
        private int _active;
        private int _count;

        public int MaxConcurrency { get; private set; }
        public int Count => _count;

        public BlockingHandler(ReleasesResponse response) => _response = response;

        public void Release() => _release.Set();
        public bool WaitStarted(TimeSpan timeout) => _started.Wait(timeout);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _active);
            lock (this) { if (current > MaxConcurrency) MaxConcurrency = current; }
            _started.Set();

            _release.Wait(cancellationToken);

            Interlocked.Decrement(ref _active);
            Interlocked.Increment(ref _count);

            var json = JsonSerializer.Serialize(_response);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

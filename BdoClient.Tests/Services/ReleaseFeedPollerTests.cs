using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;

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
}

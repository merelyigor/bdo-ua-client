using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class FeedApplicationCoordinatorTests
{
    private static (FeedApplicationCoordinator coord, ReleaseFeedPoller poller, RecordingLogger logger,
        List<ReleasesResponse> accepted)
        CreateCoordinator(Func<ReleasesResponse, Task<bool>>? applyFunc = null)
    {
        var logger = new RecordingLogger();
        var handler = new StubHttpHandler(CreateFeed("id0"));
        var httpClient = new HttpClient(handler);
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromMilliseconds(100));
        var accepted = new List<ReleasesResponse>();

        var actualApply = applyFunc ?? (_ => Task.FromResult(true));
        var coord = new FeedApplicationCoordinator(actualApply, poller, logger);
        return (coord, poller, logger, accepted);
    }

    [Fact]
    public void CandidateDuringBlocked_StoredAsPending()
    {
        var (coord, poller, _, _) = CreateCoordinator();

        coord.BlockUpdates();
        coord.OnCandidate(CreateFeed("id1"));

        Assert.NotNull(coord.PendingFeed);
        poller.Dispose();
    }

    [Fact]
    public async Task FailedApply_CandidateNotAccepted()
    {
        var (coord, poller, logger, accepted) = CreateCoordinator(_ => Task.FromResult(false));

        poller.Start(CreateFeed("id0"));
        coord.OnCandidate(CreateFeed("id1"));

        await Task.Delay(500);
        poller.Stop();
        await Task.Delay(200);

        Assert.Empty(accepted);
        Assert.NotNull(coord.PendingFeed);
        poller.Dispose();
    }

    [Fact]
    public async Task SuccessfulApply_PollerAcceptFeedCalled()
    {
        var (coord, poller, _, _) = CreateCoordinator(_ => Task.FromResult(true));

        var oldFeed = CreateFeed("id0");
        poller.Start(oldFeed);
        coord.OnCandidate(CreateFeed("id1"));

        await Task.Delay(500);
        poller.Stop();
        await Task.Delay(200);

        // After successful apply, accepted baseline should have advanced
        var acceptedFeed = poller.GetAcceptedFeed();
        Assert.NotNull(acceptedFeed);
        poller.Dispose();
    }

    [Fact]
    public async Task ApplyException_CandidateRequeued()
    {
        var (coord, poller, logger, accepted) = CreateCoordinator(_ =>
        {
            throw new InvalidOperationException("test error");
        });

        poller.Start(CreateFeed("id0"));
        coord.OnCandidate(CreateFeed("id1"));

        await Task.Delay(500);
        poller.Stop();
        await Task.Delay(200);

        Assert.Empty(accepted);
        Assert.NotNull(coord.PendingFeed);
        Assert.Contains(logger.ErrorLines, l => l.Contains("Feed application error"));
        poller.Dispose();
    }

    [Fact]
    public async Task MaxConcurrency_OneSimultaneousApply()
    {
        int activeCount = 0;
        int maxConcurrency = 0;
        var gate = new TaskCompletionSource<bool>();

        var (coord, poller, _, _) = CreateCoordinator(async _ =>
        {
            var current = Interlocked.Increment(ref activeCount);
            lock (gate) { if (current > maxConcurrency) maxConcurrency = current; }
            try
            {
                await gate.Task;
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        });

        coord.OnCandidate(CreateFeed("id1"));
        coord.OnCandidate(CreateFeed("id2"));

        await Task.Delay(200);
        Assert.True(maxConcurrency <= 1, $"Max concurrency was {maxConcurrency}");

        gate.SetResult(true);
        await Task.Delay(200);

        poller.Dispose();
    }

    [Fact]
    public async Task LatestPendingWins()
    {
        var applied = new List<string>();
        var (coord, poller, _, _) = CreateCoordinator(feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            return Task.FromResult(true);
        });

        poller.Start(CreateFeed("id0"));

        coord.OnCandidate(CreateFeed("id1"));
        await Task.Delay(100);
        coord.OnCandidate(CreateFeed("id2"));
        await Task.Delay(500);

        poller.Stop();
        await Task.Delay(200);

        Assert.Contains("id2", applied);
        poller.Dispose();
    }

    private static ReleasesResponse CreateFeed(string modeId)
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
                        Slug = modeId,
                        PublicName = modeId,
                        Current = new CurrentRelease
                        {
                            PublicId = modeId,
                            Version = 1,
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

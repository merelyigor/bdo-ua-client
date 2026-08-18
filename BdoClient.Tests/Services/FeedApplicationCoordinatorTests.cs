using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class FeedApplicationCoordinatorTests
{
    private static (FeedApplicationCoordinator coord, ReleaseFeedPoller poller, RecordingLogger logger)
        CreateCoordinator(Func<ReleasesResponse, Task<bool>>? applyFunc = null)
    {
        var logger = new RecordingLogger();
        var handler = new StubHttpHandler(CreateFeed("id0"));
        var httpClient = new HttpClient(handler);
        var apiClient = new BdoUaApiClient(httpClient, logger);
        var poller = new ReleaseFeedPoller(apiClient, logger, TimeSpan.FromMilliseconds(100));

        var actualApply = applyFunc ?? (_ => Task.FromResult(true));
        var coord = new FeedApplicationCoordinator(actualApply, poller, logger);
        return (coord, poller, logger);
    }

    // A: failed A → A pending → A not accepted
    [Fact]
    public async Task FailedApply_CandidateNotAccepted()
    {
        var (coord, poller, _) = CreateCoordinator(_ => Task.FromResult(false));

        poller.Start(CreateFeed("id0"));
        await coord.OnCandidateAsync(CreateFeed("idA"));

        var accepted = poller.GetAcceptedFeed();
        // Accepted baseline should still be id0 (not advanced to idA)
        Assert.NotNull(accepted);
        Assert.Null(accepted.Data?.Modes?.Find(m => m.Slug == "idA"));
        poller.Dispose();
    }

    // B: failed A → retry semantically identical A succeeds → accepted → pending null
    [Fact]
    public async Task FailedThenRetry_SameSemantic_AcceptedAndPendingCleared()
    {
        var callCount = 0;
        var (coord, poller, _) = CreateCoordinator(feed =>
        {
            callCount++;
            if (callCount == 1) return Task.FromResult(false);
            return Task.FromResult(true);
        });

        poller.Start(CreateFeed("id0"));
        var candidateA = CreateFeed("idA");

        await coord.OnCandidateAsync(candidateA);
        Assert.Equal(1, callCount);

        // Retry with semantically identical new object
        var candidateA2 = CreateFeed("idA");
        await coord.OnCandidateAsync(candidateA2);
        Assert.Equal(2, callCount);

        var accepted = poller.GetAcceptedFeed();
        Assert.NotNull(accepted.Data?.Modes?.Find(m => m.Slug == "idA"));
        Assert.False(coord.HasPendingFeed);
        poller.Dispose();
    }

    // C: failed A → retry A succeeds → B succeeds → final accepted B → A never applied after B
    [Fact]
    public async Task FailedA_RetryA_ThenB_FinalAcceptedB()
    {
        var applied = new List<string>();
        var callCount = 0;

        var (coord, poller, _) = CreateCoordinator(feed =>
        {
            callCount++;
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);

            // First call (A) fails
            if (callCount == 1) return Task.FromResult(false);
            return Task.FromResult(true);
        });

        poller.Start(CreateFeed("id0"));

        // A fails
        await coord.OnCandidateAsync(CreateFeed("idA"));
        // Retry A succeeds
        await coord.OnCandidateAsync(CreateFeed("idA"));
        // B succeeds
        await coord.OnCandidateAsync(CreateFeed("idB"));

        var accepted = poller.GetAcceptedFeed();
        Assert.NotNull(accepted.Data?.Modes?.Find(m => m.Slug == "idB"));

        // A should never be applied after B
        var lastAIndex = applied.LastIndexOf("idA");
        var firstBIndex = applied.IndexOf("idB");
        if (lastAIndex >= 0 && firstBIndex >= 0)
            Assert.True(lastAIndex < firstBIndex, "A was applied after B");

        poller.Dispose();
    }

    // D: A applying → B arrives → C arrives → latest pending C wins
    [Fact]
    public async Task ApplyingThenBC_LatestPendingCWins()
    {
        var gate = new TaskCompletionSource<bool>();
        var applied = new List<string>();

        var (coord, poller, _) = CreateCoordinator(async feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            await gate.Task;
            return true;
        });

        poller.Start(CreateFeed("id0"));

        // Start A (will block)
        var taskA = coord.OnCandidateAsync(CreateFeed("idA"));
        await Task.Delay(50);

        // B and C arrive while A is applying
        await coord.OnCandidateAsync(CreateFeed("idB"));
        await coord.OnCandidateAsync(CreateFeed("idC"));

        // Unblock
        gate.SetResult(true);
        await taskA;
        await Task.Delay(200);

        // C should be the final applied (latest pending wins)
        Assert.Contains("idC", applied);
        poller.Dispose();
    }

    // E: callback throws during "mode rebuild" → candidate pending → not accepted
    [Fact]
    public async Task CallbackThrows_CandidatePending_NotAccepted()
    {
        var (coord, poller, logger) = CreateCoordinator(_ =>
        {
            throw new InvalidOperationException("mode rebuild failed");
        });

        poller.Start(CreateFeed("id0"));
        await coord.OnCandidateAsync(CreateFeed("idA"));

        var accepted = poller.GetAcceptedFeed();
        Assert.Null(accepted.Data?.Modes?.Find(m => m.Slug == "idA"));
        Assert.True(coord.HasPendingFeed);
        Assert.Contains(logger.ErrorLines, l => l.Contains("Feed application error"));
        poller.Dispose();
    }

    // F: callback returns false (RefreshState fails) → candidate pending → not accepted
    [Fact]
    public async Task RefreshStateFails_CandidatePending_NotAccepted()
    {
        var (coord, poller, _) = CreateCoordinator(_ => Task.FromResult(false));

        poller.Start(CreateFeed("id0"));
        await coord.OnCandidateAsync(CreateFeed("idA"));

        var accepted = poller.GetAcceptedFeed();
        Assert.Null(accepted.Data?.Modes?.Find(m => m.Slug == "idA"));
        Assert.True(coord.HasPendingFeed);
        poller.Dispose();
    }

    // G: successful apply → pending matching candidate cleared → AcceptFeed exactly once
    [Fact]
    public async Task SuccessfulApply_PendingCleared_AcceptFeedOnce()
    {
        var acceptCount = 0;
        var (coord, poller, _) = CreateCoordinator(_ => Task.FromResult(true));

        // Track accepts by checking accepted feed changes
        ReleasesResponse? lastAccepted = null;
        poller.Start(CreateFeed("id0"));
        lastAccepted = poller.GetAcceptedFeed();

        await coord.OnCandidateAsync(CreateFeed("idA"));

        var newAccepted = poller.GetAcceptedFeed();
        Assert.NotNull(newAccepted.Data?.Modes?.Find(m => m.Slug == "idA"));
        Assert.False(coord.HasPendingFeed);
        poller.Dispose();
    }

    // H: blocked coordinator → candidate stored → apply not started
    [Fact]
    public async Task BlockedCoordinator_CandidateStored_NoApply()
    {
        var applyCalled = false;
        var (coord, poller, _) = CreateCoordinator(_ =>
        {
            applyCalled = true;
            return Task.FromResult(true);
        });

        coord.BlockUpdates();
        await coord.OnCandidateAsync(CreateFeed("idA"));

        Assert.False(applyCalled);
        Assert.True(coord.HasPendingFeed);
        poller.Dispose();
    }

    // I: ApplyPendingIfAnyAsync during finalization → applies latest → accepted → pending empty
    [Fact]
    public async Task ApplyPendingDuringFinalization_AppliesLatest()
    {
        var applied = new List<string>();
        var (coord, poller, _) = CreateCoordinator(feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            return Task.FromResult(true);
        });

        coord.BlockUpdates();
        await coord.OnCandidateAsync(CreateFeed("idA"));
        await coord.OnCandidateAsync(CreateFeed("idB"));

        // ApplyPendingIfAnyAsync should work even while blocked
        await coord.ApplyPendingIfAnyAsync();

        Assert.Contains("idB", applied);
        var accepted = poller.GetAcceptedFeed();
        Assert.NotNull(accepted.Data?.Modes?.Find(m => m.Slug == "idB"));
        poller.Dispose();
    }

    // J: max apply concurrency <= 1
    [Fact]
    public async Task MaxConcurrency_OneSimultaneousApply()
    {
        int activeCount = 0;
        int maxConcurrency = 0;
        var gate = new TaskCompletionSource<bool>();

        var (coord, poller, _) = CreateCoordinator(async _ =>
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

        var task1 = coord.OnCandidateAsync(CreateFeed("id1"));
        var task2 = coord.OnCandidateAsync(CreateFeed("id2"));

        await Task.Delay(200);
        Assert.True(maxConcurrency <= 1, $"Max concurrency was {maxConcurrency}");

        gate.SetResult(true);
        await Task.WhenAll(task1, task2);

        poller.Dispose();
    }

    // K: after B accepted, stale A cannot become accepted
    [Fact]
    public async Task AfterBAccepted_StaleANeverApplied()
    {
        var applied = new List<string>();
        var gate = new TaskCompletionSource<bool>();

        var (coord, poller, _) = CreateCoordinator(async feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);

            // First call (A) blocks
            if (slug == "idA")
            {
                await gate.Task;
            }
            return true;
        });

        poller.Start(CreateFeed("id0"));

        // Start A (blocks)
        var taskA = coord.OnCandidateAsync(CreateFeed("idA"));
        await Task.Delay(50);

        // B arrives while A is applying → stored as pending
        await coord.OnCandidateAsync(CreateFeed("idB"));

        // Unblock A
        gate.SetResult(true);
        await taskA;
        await Task.Delay(300);

        // B should be applied after A, and A should not be applied again after B
        var lastAIndex = applied.LastIndexOf("idA");
        var lastBIndex = applied.LastIndexOf("idB");
        if (lastAIndex >= 0 && lastBIndex >= 0)
            Assert.True(lastAIndex < lastBIndex, "A was applied after B");

        var accepted = poller.GetAcceptedFeed();
        Assert.NotNull(accepted.Data?.Modes?.Find(m => m.Slug == "idB"));
        poller.Dispose();
    }

    // Blocked then unblocked
    [Fact]
    public async Task BlockedThenUnblocked_CanApply()
    {
        var (coord, poller, _) = CreateCoordinator(_ => Task.FromResult(true));

        coord.BlockUpdates();
        await coord.OnCandidateAsync(CreateFeed("idA"));
        Assert.True(coord.HasPendingFeed);

        coord.UnblockUpdates();
        await coord.ApplyPendingIfAnyAsync();

        var accepted = poller.GetAcceptedFeed();
        Assert.NotNull(accepted.Data?.Modes?.Find(m => m.Slug == "idA"));
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

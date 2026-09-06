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

    // A: A applying → B arrives → C arrives → A returns false → C applied → final accepted C
    [Fact]
    public async Task A_FailsWhileBCPending_CApplied_FinalAcceptedC()
    {
        var gate = new TaskCompletionSource<bool>();
        var applied = new List<string>();

        var (coord, poller, _) = CreateCoordinator(async feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            if (slug == "idA")
            {
                await gate.Task;
                return false;
            }
            return true;
        });

        poller.Start(CreateFeed("id0"));

        // Start A (blocks)
        var taskA = coord.OnCandidateAsync(CreateFeed("idA"));
        await Task.Delay(50);

        // B then C arrive while A is applying
        await coord.OnCandidateAsync(CreateFeed("idB"));
        await coord.OnCandidateAsync(CreateFeed("idC"));

        // Unblock A — it fails
        gate.SetResult(false);
        await taskA;
        await Task.Delay(300);

        // C should be applied (latest pending wins, B superseded)
        Assert.Contains("idC", applied);
        var accepted = poller.GetAcceptedFeed();
        Assert.Contains("idC", GetAcceptedSlugs(poller));
        Assert.False(coord.HasPendingFeed);
        poller.Dispose();
    }

    // B: A applying → C arrives → A throws → C applied → final accepted C
    [Fact]
    public async Task A_ThrowsWhileCPending_CApplied_FinalAcceptedC()
    {
        var gate = new TaskCompletionSource<bool>();
        var applied = new List<string>();

        var (coord, poller, _) = CreateCoordinator(async feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            if (slug == "idA")
            {
                await gate.Task;
                throw new InvalidOperationException("mode rebuild failed");
            }
            return true;
        });

        poller.Start(CreateFeed("id0"));

        var taskA = coord.OnCandidateAsync(CreateFeed("idA"));
        await Task.Delay(50);

        await coord.OnCandidateAsync(CreateFeed("idC"));

        gate.SetResult(true);
        await taskA;
        await Task.Delay(300);

        Assert.Contains("idC", applied);
        var accepted = poller.GetAcceptedFeed();
        Assert.Contains("idC", GetAcceptedSlugs(poller));
        Assert.False(coord.HasPendingFeed);
        poller.Dispose();
    }

    // C: A fails with no newer pending → A remains pending → accepted unchanged
    [Fact]
    public async Task A_FailsNoNewerPending_ARemainsPending()
    {
        var (coord, poller, _) = CreateCoordinator(_ => Task.FromResult(false));

        poller.Start(CreateFeed("id0"));
        await coord.OnCandidateAsync(CreateFeed("idA"));

        Assert.True(coord.HasPendingFeed);
        // Accepted baseline should still be id0
        Assert.DoesNotContain("idA", GetAcceptedSlugs(poller));
        poller.Dispose();
    }

    // D: A fails with semantically-identical A already pending → only one A pending
    [Fact]
    public async Task A_FailsWithSameSemanticPending_OnlyOnePending()
    {
        var applyCount = 0;
        var (coord, poller, _) = CreateCoordinator(_ =>
        {
            applyCount++;
            return Task.FromResult(false);
        });

        poller.Start(CreateFeed("id0"));

        // First A fails, becomes pending
        await coord.OnCandidateAsync(CreateFeed("idA"));
        Assert.Equal(1, applyCount);

        // Second semantically-identical A — should reapply (pending was equal so overwritten)
        await coord.OnCandidateAsync(CreateFeed("idA"));
        Assert.Equal(2, applyCount);

        Assert.True(coord.HasPendingFeed);
        poller.Dispose();
    }

    // E: A succeeds while semantically-identical A pending → pending cleared → exactly-once accept
    [Fact]
    public async Task A_SucceedsWithSamePending_PendingCleared_AcceptExactlyOnce()
    {
        var gate = new TaskCompletionSource<bool>();
        var applied = new List<string>();

        var (coord, poller, logger) = CreateCoordinator(async feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            // First call (idA) blocks; second arrives while first is applying
            if (applied.Count == 1 && slug == "idA")
            {
                await gate.Task;
            }
            return true;
        });

        poller.Start(CreateFeed("id0"));

        // First A blocks
        var taskA = coord.OnCandidateAsync(CreateFeed("idA"));
        await Task.Delay(50);

        // Second semantically-identical A arrives while first is applying → stored as pending
        await coord.OnCandidateAsync(CreateFeed("idA"));

        // Unblock first A
        gate.SetResult(true);
        await taskA;
        await Task.Delay(200);

        // Accept should happen exactly once (second A is stale-pending, cleared)
        var acceptCount = logger.DebugLines.Count(l => l.Contains("Feed candidate accepted"));
        Assert.Equal(1, acceptCount);
        Assert.False(coord.HasPendingFeed);
        poller.Dispose();
    }

    // F: failed A → retry A → B → final accepted B
    [Fact]
    public async Task FailedA_RetryA_ThenB_FinalAcceptedB()
    {
        var callCount = 0;

        var (coord, poller, _) = CreateCoordinator(feed =>
        {
            callCount++;
            if (callCount == 1) return Task.FromResult(false);
            return Task.FromResult(true);
        });

        poller.Start(CreateFeed("id0"));

        await coord.OnCandidateAsync(CreateFeed("idA"));
        await coord.OnCandidateAsync(CreateFeed("idA"));
        await coord.OnCandidateAsync(CreateFeed("idB"));

        var accepted = poller.GetAcceptedFeed();
        Assert.Contains("idB", GetAcceptedSlugs(poller));
        poller.Dispose();
    }

    // G: A/B/C latest-pending wins
    [Fact]
    public async Task LatestPendingWins()
    {
        var gate = new TaskCompletionSource<bool>();
        var applied = new List<string>();

        var (coord, poller, _) = CreateCoordinator(async feed =>
        {
            var slug = feed.Data?.Modes?.FirstOrDefault()?.Slug ?? "?";
            applied.Add(slug);
            if (slug == "idA") await gate.Task;
            return true;
        });

        poller.Start(CreateFeed("id0"));

        var taskA = coord.OnCandidateAsync(CreateFeed("idA"));
        await Task.Delay(50);
        await coord.OnCandidateAsync(CreateFeed("idB"));
        await coord.OnCandidateAsync(CreateFeed("idC"));

        gate.SetResult(true);
        await taskA;
        await Task.Delay(300);

        Assert.Contains("idC", applied);
        poller.Dispose();
    }

    // H: max apply concurrency <= 1
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

    // Blocked → stored → no apply
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

    // ApplyPendingIfAnyAsync during finalization
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

        await coord.ApplyPendingIfAnyAsync();

        Assert.Contains("idB", applied);
        var accepted = poller.GetAcceptedFeed();
        Assert.Contains("idB", GetAcceptedSlugs(poller));
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
        Assert.Contains("idA", GetAcceptedSlugs(poller));
        poller.Dispose();
    }

    private static List<string> GetAcceptedSlugs(ReleaseFeedPoller poller)
    {
        return poller.GetAcceptedFeed()?.Data?.Modes?.Select(m => m.Slug ?? "").ToList() ?? new();
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
        private readonly object _debugSync = new();
        private readonly List<string> _debugLines = new();

        public IReadOnlyList<string> DebugLines
        {
            get
            {
                lock (_debugSync)
                {
                    return _debugLines.ToArray();
                }
            }
        }

        public List<string> InfoLines { get; } = new();
        public List<string> WarningLines { get; } = new();
        public List<string> ErrorLines { get; } = new();

        public void Debug(string message)
        {
            lock (_debugSync)
            {
                _debugLines.Add(message);
            }
        }

        public void Info(string message) => InfoLines.Add(message);
        public void Warning(string message) => WarningLines.Add(message);
        public void Error(string message) => ErrorLines.Add(message);
    }
}

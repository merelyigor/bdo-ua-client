using System.Diagnostics;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Services;

public sealed class ReleaseFeedPoller : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    private readonly BdoUaApiClient _apiClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<ReleasesResponse>? OnFeedChanged;
    public event Action<string>? OnPollFailed;

    public ReleaseFeedPoller(BdoUaApiClient apiClient, ILogger logger, TimeSpan? pollInterval = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public void Start(ReleasesResponse? currentFeed)
    {
        if (_loopTask != null) return;

        _cts = new CancellationTokenSource();
        var capturedFeed = currentFeed;
        _loopTask = RunLoopAsync(capturedFeed, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void UpdateCurrentFeed(ReleasesResponse? feed)
    {
        _currentFeedSnapshot = feed;
    }

    private volatile ReleasesResponse? _currentFeedSnapshot;

    public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;

    private async Task RunLoopAsync(ReleasesResponse? initialFeed, CancellationToken cancellationToken)
    {
        _currentFeedSnapshot = initialFeed;
        _logger.Debug("Release feed poller started.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (cancellationToken.IsCancellationRequested) break;

                var sw = Stopwatch.StartNew();
                var result = await _apiClient.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (cancellationToken.IsCancellationRequested) break;

                if (result.IsSuccess && result.Value?.Data?.Modes != null)
                {
                    var newFeed = result.Value;
                    var changed = FeedChangeDetector.HasSemanticChange(_currentFeedSnapshot, newFeed);

                    if (changed)
                    {
                        _logger.Debug($"Release feed changed (poll took {sw.ElapsedMilliseconds}ms). Notifying UI.");
                        _currentFeedSnapshot = newFeed;
                        OnFeedChanged?.Invoke(newFeed);
                    }
                    else
                    {
                        _logger.Debug($"Release feed unchanged (poll took {sw.ElapsedMilliseconds}ms).");
                    }
                }
                else
                {
                    var errorKind = result.ErrorKind;
                    var errorMsg = result.ErrorMessage;
                    _logger.Debug($"Background poll failed: {errorKind} — {errorMsg}. Keeping last known feed.");
                    OnPollFailed?.Invoke(errorMsg ?? "Poll failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.Error($"Release feed poller error: {ex.Message}");
        }
        finally
        {
            _logger.Debug("Release feed poller stopped.");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }
}

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
    private bool _paused;
    private bool _disposed;

    private volatile ReleasesResponse? _acceptedFeed;

    public event Action<ReleasesResponse>? OnFeedCandidate;
    public event Action<string>? OnPollFailed;

    public ReleaseFeedPoller(BdoUaApiClient apiClient, ILogger logger, TimeSpan? pollInterval = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;

    public void Start(ReleasesResponse? acceptedFeed)
    {
        if (_disposed) return;
        if (_loopTask != null) return;

        _acceptedFeed = acceptedFeed;
        _paused = false;
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Pause()
    {
        _paused = true;
    }

    public void Resume()
    {
        _paused = false;
    }

    public void AcceptFeed(ReleasesResponse feed)
    {
        _acceptedFeed = feed;
    }

    public ReleasesResponse? GetAcceptedFeed() => _acceptedFeed;

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
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
                if (_paused)
                {
                    _logger.Debug("Poller paused. Skipping poll.");
                    continue;
                }

                var sw = Stopwatch.StartNew();
                var result = await _apiClient.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (cancellationToken.IsCancellationRequested) break;

                if (result.IsSuccess && result.Value?.Data?.Modes != null)
                {
                    var candidate = result.Value;
                    var changed = FeedChangeDetector.HasSemanticChange(_acceptedFeed, candidate);

                    if (changed)
                    {
                        _logger.Debug($"Release feed changed (poll took {sw.ElapsedMilliseconds}ms). Notifying consumer.");
                        OnFeedCandidate?.Invoke(candidate);
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
                    _logger.Debug($"Background poll failed: {errorKind} — {errorMsg}. Keeping accepted feed.");
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
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }
}

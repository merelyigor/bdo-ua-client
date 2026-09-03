using System.Diagnostics;
using System.Threading;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Services;

public enum ReleaseFeedPollingMode
{
    Visible,
    Background
}

public sealed class ReleaseFeedPoller : IDisposable
{
    private static readonly TimeSpan DefaultVisiblePollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultBackgroundPollInterval = TimeSpan.FromMinutes(5);

    private readonly BdoUaApiClient _apiClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _visibleInterval;
    private readonly TimeSpan _backgroundInterval;

    private readonly object _schedulerLock = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _schedulerWaitCts;
    private Task? _loopTask;
    private volatile bool _disposed;
    private bool _paused;
    private bool _immediateRequested;
    private bool _pollInProgress;
    private ReleaseFeedPollingMode _mode = ReleaseFeedPollingMode.Visible;

    private volatile ReleasesResponse? _acceptedFeed;

    public event Action<ReleasesResponse>? OnFeedCandidate;
    public event Action<string>? OnPollFailed;

    public ReleaseFeedPoller(
        BdoUaApiClient apiClient,
        ILogger logger,
        TimeSpan? visiblePollInterval = null,
        TimeSpan? backgroundPollInterval = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _visibleInterval = visiblePollInterval ?? DefaultVisiblePollInterval;
        _backgroundInterval = backgroundPollInterval ?? DefaultBackgroundPollInterval;
    }

    public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;

    public void Start(ReleasesResponse? acceptedFeed)
    {
        if (_disposed) return;
        if (_loopTask != null) return;

        _acceptedFeed = acceptedFeed;
        lock (_schedulerLock)
        {
            _paused = false;
            _immediateRequested = false;
        }

        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        WakeSchedulerWait();
    }

    public void Pause()
    {
        if (_disposed) return;
        lock (_schedulerLock)
        {
            _paused = true;
            _immediateRequested = false;
        }
        WakeSchedulerWait();
    }

    public void Resume()
    {
        if (_disposed) return;
        bool wasPaused;
        lock (_schedulerLock)
        {
            wasPaused = _paused;
            _paused = false;
        }

        if (wasPaused)
            WakeSchedulerWait();
    }

    public void SetPollingMode(ReleaseFeedPollingMode mode)
    {
        if (_disposed) return;
        bool changed;
        lock (_schedulerLock)
        {
            changed = _mode != mode;
            _mode = mode;
        }

        if (changed)
            WakeSchedulerWait();
    }

    public void RequestImmediatePoll()
    {
        if (_disposed) return;
        if (!IsRunning) return;

        bool canRequest;
        lock (_schedulerLock)
        {
            canRequest = !_paused && !_pollInProgress;
            if (canRequest)
                _immediateRequested = true;
        }

        if (canRequest)
            WakeSchedulerWait();
    }

    public void AcceptFeed(ReleasesResponse feed)
    {
        _acceptedFeed = feed;
    }

    public ReleasesResponse? GetAcceptedFeed() => _acceptedFeed;

    private void WakeSchedulerWait()
    {
        CancellationTokenSource? cts;
        lock (_schedulerLock)
        {
            cts = _schedulerWaitCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Loop already released/disposed the current wait CTS; safe to ignore.
        }
    }

    private void ReleaseSchedulerWait(CancellationTokenSource? waitCts)
    {
        if (waitCts == null) return;

        lock (_schedulerLock)
        {
            if (ReferenceEquals(_schedulerWaitCts, waitCts))
                _schedulerWaitCts = null;
        }

        waitCts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        _logger.Debug("Release feed poller started.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReleaseFeedPollingMode mode;
                TimeSpan interval;
                bool paused;
                bool immediate;

                lock (_schedulerLock)
                {
                    mode = _mode;
                    interval = mode == ReleaseFeedPollingMode.Background ? _backgroundInterval : _visibleInterval;
                    paused = _paused;
                    immediate = _immediateRequested;
                }

                if (immediate)
                {
                    // No scheduler wait CTS is needed for an immediate poll.
                    lock (_schedulerLock)
                    {
                        _immediateRequested = false;
                    }
                }
                else
                {
                    CancellationTokenSource? waitCts = null;
                    try
                    {
                        waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        lock (_schedulerLock)
                        {
                            _schedulerWaitCts = waitCts;
                        }

                        if (paused)
                        {
                            // Wait indefinitely; no network poll while paused.
                            try
                            {
                                await Task.Delay(Timeout.InfiniteTimeSpan, waitCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                // Woken by Resume / mode change / dropped immediate.
                            }

                            continue;
                        }

                        try
                        {
                            await Task.Delay(interval, waitCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Woken by mode change / immediate / Pause -> re-evaluate.
                            continue;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        ReleaseSchedulerWait(waitCts);
                    }

                    if (cancellationToken.IsCancellationRequested) break;
                }

                if (cancellationToken.IsCancellationRequested) break;

                bool doPoll;
                lock (_schedulerLock)
                {
                    doPoll = !_paused && !_disposed && !cancellationToken.IsCancellationRequested;
                    if (doPoll)
                    {
                        _pollInProgress = true;
                        _immediateRequested = false;
                    }
                }

                if (!doPoll) continue;

                try
                {
                    await PerformPollAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (_schedulerLock)
                    {
                        _pollInProgress = false;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
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

    private async Task PerformPollAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await _apiClient.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (cancellationToken.IsCancellationRequested) return;

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        WakeSchedulerWait();
        _cts?.Dispose();
        _loopTask = null;
    }
}

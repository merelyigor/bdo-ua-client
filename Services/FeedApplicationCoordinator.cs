using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Services;

public sealed class FeedApplicationCoordinator
{
    private readonly Func<ReleasesResponse, Task<bool>> _applyFeed;
    private readonly ILogger _logger;
    private readonly ReleaseFeedPoller _poller;

    private volatile bool _feedApplyInProgress;
    private volatile bool _blocked;
    private ReleasesResponse? _pendingFeed;

    public FeedApplicationCoordinator(
        Func<ReleasesResponse, Task<bool>> applyFeed,
        ReleaseFeedPoller poller,
        ILogger logger)
    {
        _applyFeed = applyFeed ?? throw new ArgumentNullException(nameof(applyFeed));
        _poller = poller ?? throw new ArgumentNullException(nameof(poller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsApplying => _feedApplyInProgress;
    public bool IsBlocked => _blocked;
    public bool HasPendingFeed => _pendingFeed != null;

    public void BlockUpdates() => _blocked = true;
    public void UnblockUpdates() => _blocked = false;

    /// <summary>
    /// Entry point from poller event. If blocked or applying, stores as pending.
    /// Otherwise starts serialized application. Caller should await for no-unobserved-task.
    /// </summary>
    public async Task OnCandidateAsync(ReleasesResponse candidate)
    {
        if (_blocked || _feedApplyInProgress)
        {
            _logger.Debug("Feed candidate received while busy. Storing as pending.");
            _pendingFeed = candidate;
            return;
        }

        await RunApplyAsync(candidate);
    }

    /// <summary>
    /// Applies latest pending candidate if any. Used during operation finalization.
    /// Can be called while blocked (explicit finalization path).
    /// </summary>
    public async Task ApplyPendingIfAnyAsync()
    {
        if (_pendingFeed != null && !_feedApplyInProgress)
        {
            var pending = _pendingFeed;
            _pendingFeed = null;
            await RunApplyAsync(pending);
        }
    }

    private async Task RunApplyAsync(ReleasesResponse candidate)
    {
        if (_feedApplyInProgress) { _pendingFeed = candidate; return; }

        _feedApplyInProgress = true;
        try
        {
            while (candidate != null)
            {
                bool success = false;
                try
                {
                    success = await _applyFeed(candidate);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Feed application error: {ex.Message}. Candidate requeued.");
                }

                if (success)
                {
                    _poller.AcceptFeed(candidate);
                    _logger.Debug("Feed candidate accepted after successful application.");

                    // Clear stale pending: if pending is semantically identical
                    // to the just-accepted candidate, clear it to prevent regression.
                    ClearStalePending(candidate);
                }
                else
                {
                    // Only requeue failed candidate if no newer different pending exists.
                    // Preserve already-arrived newer candidate that supersedes this failure.
                    var existing = _pendingFeed;
                    if (existing == null || IsSemanticallyEqual(existing, candidate))
                    {
                        _pendingFeed = candidate;
                    }
                    else
                    {
                        _logger.Debug("Failed candidate superseded by newer pending feed.");
                    }
                }

                var next = _pendingFeed;
                if (next != null && !IsSemanticallyEqual(next, candidate))
                {
                    candidate = next;
                    _pendingFeed = null;
                    _logger.Debug("Processing next pending feed candidate.");
                }
                else
                {
                    break;
                }
            }
        }
        finally
        {
            _feedApplyInProgress = false;
        }
    }

    /// <summary>
    /// After successful acceptance of candidate X, if pending is semantically
    /// identical to X, clear it. Prevents stale-pending regression where
    /// accepted B could be followed by stale A.
    /// </summary>
    private void ClearStalePending(ReleasesResponse accepted)
    {
        var pending = _pendingFeed;
        if (pending != null && IsSemanticallyEqual(pending, accepted))
        {
            _pendingFeed = null;
            _logger.Debug("Cleared stale pending feed matching accepted candidate.");
        }
    }

    /// <summary>
    /// Two feed candidates are semantically equal if they have the same
    /// UI-relevant content (ignoring GeneratedAt).
    /// </summary>
    private static bool IsSemanticallyEqual(ReleasesResponse a, ReleasesResponse b)
    {
        return !FeedChangeDetector.HasSemanticChange(a, b);
    }
}

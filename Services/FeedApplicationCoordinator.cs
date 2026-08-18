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
    public ReleasesResponse? PendingFeed => _pendingFeed;

    public void BlockUpdates()
    {
        _blocked = true;
    }

    public void UnblockUpdates()
    {
        _blocked = false;
    }

    public void OnCandidate(ReleasesResponse candidate)
    {
        if (_blocked || _feedApplyInProgress)
        {
            _logger.Debug("Feed candidate received while busy. Storing as pending.");
            _pendingFeed = candidate;
            return;
        }

        _ = RunApplyAsync(candidate);
    }

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
                }
                else
                {
                    _pendingFeed = candidate;
                }

                var next = _pendingFeed;
                if (next != null && next != candidate)
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
}

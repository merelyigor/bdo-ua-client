namespace BdoClient.Services;

internal sealed class LocalizationNotificationTracker
{
    private bool _actionableEpisodeActive;

    public void Reset() => _actionableEpisodeActive = false;

    public bool Observe(LocalizationState state, bool canNotify)
    {
        if (state != LocalizationState.UpdateAvailable)
        {
            _actionableEpisodeActive = false;
            return false;
        }

        if (_actionableEpisodeActive)
            return false;

        _actionableEpisodeActive = true;
        return canNotify;
    }
}

using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class LocalizationNotificationTrackerTests
{
    [Fact]
    public void UpToDate_DoesNotNotify()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.False(tracker.Observe(LocalizationState.UpToDate, canNotify: true));
    }

    [Fact]
    public void FirstHiddenUpdateAvailable_Notifies()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void FirstVisibleUpdateAvailable_DoesNotNotifyButLatches()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.False(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: false));
        Assert.False(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void RepeatedHiddenUpdateAvailable_DoesNotNotify()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
        Assert.False(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void VisibleThenHiddenUpdateAvailable_DoesNotNotify()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe(LocalizationState.UpdateAvailable, canNotify: false);

        Assert.False(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void UpdateAvailableThenUpToDate_Resets()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true);

        Assert.False(tracker.Observe(LocalizationState.UpToDate, canNotify: true));
    }

    [Fact]
    public void AfterReset_NewHiddenUpdateAvailable_NotifiesAgain()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true);
        tracker.Observe(LocalizationState.UpToDate, canNotify: true);

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void WaitingForReleaseThenHiddenUpdateAvailable_Notifies()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe(LocalizationState.WaitingForRelease, canNotify: true);

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void NotInstalledThenHiddenUpdateAvailable_Notifies()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe(LocalizationState.NotInstalled, canNotify: true);

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void UpdateAvailableThenWaitingForRelease_Resets()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true);
        tracker.Observe(LocalizationState.WaitingForRelease, canNotify: true);

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }

    [Fact]
    public void RepeatedState_DoesNotDependOnPublicIdOrMode()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
        Assert.False(tracker.Observe(LocalizationState.UpdateAvailable, canNotify: true));
    }
}

using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class StartupUpdateLifecycleCoordinatorTests
{
    [Fact]
    public async Task RunAsync_StartsDiscoveryOnlyAfterMaintenanceCompletes()
    {
        var maintenanceCompleted = false;
        var discoveryStarted = false;

        await StartupUpdateLifecycleCoordinator.RunAsync(
            () =>
            {
                maintenanceCompleted = true;
                return Task.CompletedTask;
            },
            () => false,
            () => discoveryStarted = maintenanceCompleted,
            _ => throw new InvalidOperationException("Maintenance should succeed"));

        Assert.True(maintenanceCompleted);
        Assert.True(discoveryStarted);
    }

    [Fact]
    public async Task RunAsync_ContainsMaintenanceFailure_ThenStartsDiscovery()
    {
        Exception? reported = null;
        var discoveryStarted = false;

        await StartupUpdateLifecycleCoordinator.RunAsync(
            () => Task.FromException(new InvalidOperationException("maintenance failed")),
            () => false,
            () => discoveryStarted = true,
            ex => reported = ex);

        Assert.NotNull(reported);
        Assert.True(discoveryStarted);
    }

    [Fact]
    public async Task RunAsync_ClosingDuringMaintenance_DoesNotStartDiscovery()
    {
        var closing = false;
        var discoveryStarted = false;

        await StartupUpdateLifecycleCoordinator.RunAsync(
            () =>
            {
                closing = true;
                return Task.CompletedTask;
            },
            () => closing,
            () => discoveryStarted = true,
            _ => throw new InvalidOperationException("Maintenance should succeed"));

        Assert.False(discoveryStarted);
    }
}

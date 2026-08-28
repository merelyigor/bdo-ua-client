using System;
using System.Threading;
using BdoClient.Services;
using Xunit;

namespace BdoClient.Tests.Services;

public class SingleInstanceCoordinatorTests
{
    private static (string Mutex, string Event) UniqueNames()
    {
        var g = Guid.NewGuid().ToString("N");
        return (@$"Local\{g}", @$"Local\{g}-activate");
    }

    [Fact]
    public void First_coordinator_for_unique_name_is_primary()
    {
        var (mutex, evt) = UniqueNames();

        using var coordinator = new SingleInstanceCoordinator(mutex, evt);

        Assert.True(coordinator.IsPrimary);
    }

    [Fact]
    public void Second_coordinator_for_same_live_name_is_secondary()
    {
        var (mutex, evt) = UniqueNames();

        using var primary = new SingleInstanceCoordinator(mutex, evt);
        Assert.True(primary.IsPrimary);

        SingleInstanceCoordinator? secondary = null;
        var ready = new ManualResetEventSlim(false);

        var worker = new Thread(() =>
        {
            secondary = new SingleInstanceCoordinator(mutex, evt);
            ready.Set();
        });
        worker.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.NotNull(secondary);
            Assert.False(secondary!.IsPrimary);
        }
        finally
        {
            secondary?.Dispose();
        }
    }

    [Fact]
    public void Secondary_can_signal_primary_activation_callback()
    {
        var (mutex, evt) = UniqueNames();

        using var primary = new SingleInstanceCoordinator(mutex, evt);
        var fired = new ManualResetEventSlim(false);
        primary.RegisterActivationCallback(() => fired.Set());

        using (var secondary = new SingleInstanceCoordinator(mutex, evt))
        {
            Assert.False(secondary.IsPrimary);
            secondary.SignalActivation();
        }

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void One_activation_signal_produces_one_callback()
    {
        var (mutex, evt) = UniqueNames();

        using var primary = new SingleInstanceCoordinator(mutex, evt);
        var count = 0;
        primary.RegisterActivationCallback(() => Interlocked.Increment(ref count));

        using (var secondary = new SingleInstanceCoordinator(mutex, evt))
        {
            secondary.SignalActivation();
        }

        Thread.Sleep(TimeSpan.FromSeconds(1));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Coordinator_with_different_unique_name_is_independently_primary()
    {
        var a = UniqueNames();
        var b = UniqueNames();

        using var first = new SingleInstanceCoordinator(a.Mutex, a.Event);
        using var second = new SingleInstanceCoordinator(b.Mutex, b.Event);

        Assert.True(first.IsPrimary);
        Assert.True(second.IsPrimary);
    }

    [Fact]
    public void After_owners_disposed_fresh_coordinator_becomes_primary()
    {
        var (mutex, evt) = UniqueNames();

        using (var primary = new SingleInstanceCoordinator(mutex, evt))
        {
            Assert.True(primary.IsPrimary);
        }

        using var fresh = new SingleInstanceCoordinator(mutex, evt);
        Assert.True(fresh.IsPrimary);
    }

    [Fact]
    public void Secondary_does_not_own_or_release_primary_mutex()
    {
        var (mutex, evt) = UniqueNames();

        using var primary = new SingleInstanceCoordinator(mutex, evt);
        Assert.True(primary.IsPrimary);

        SingleInstanceCoordinator? secondary = null;
        var ready = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            secondary = new SingleInstanceCoordinator(mutex, evt);
            ready.Set();
        });
        worker.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.NotNull(secondary);
            Assert.False(secondary!.IsPrimary);
            // Releasing would throw if it wrongly believed it owned the mutex.
            secondary.Dispose();
        }
        finally
        {
            secondary?.Dispose();
        }

        // Primary must still hold ownership after the secondary released nothing.
        Assert.True(primary.IsPrimary);
    }

    [Fact]
    public void Disposal_is_safe_and_idempotent()
    {
        var (mutex, evt) = UniqueNames();

        using var coordinator = new SingleInstanceCoordinator(mutex, evt);
        coordinator.Dispose();
        coordinator.Dispose();
    }
}

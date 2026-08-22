using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BdoClient.Api;
using BdoClient.Logging;

namespace BdoClient.Tests.Api;

public sealed class ResilientConnectionConnectorTests
{
    private static readonly IPAddress AddressA = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress AddressB = IPAddress.Parse("198.51.100.2");
    private static readonly IPAddress AddressC = IPAddress.Parse("203.0.113.3");

    [Fact]
    public void OrderCandidates_InterleavesAddressFamiliesPreservingResolverOrder()
    {
        var ipv6First = IPAddress.Parse("2001:db8::1");
        var ipv6Second = IPAddress.Parse("2001:db8::2");
        var ordered = ResilientConnectionConnector.OrderCandidates(
            new[] { ipv6First, AddressA, ipv6Second, AddressB });

        Assert.Equal(new[] { ipv6First, AddressA, ipv6Second, AddressB }, ordered);
    }

    [Fact]
    public async Task FirstAddressSucceeds_ReturnsFirstStream()
    {
        var calls = new List<IPAddress>();
        using var expected = new TrackingStream();
        var connector = CreateConnector(
            new[] { AddressA, AddressB },
            async (address, _, _) =>
            {
                calls.Add(address);
                await Task.Yield();
                return expected;
            });

        var result = await connector.ConnectAsync(new DnsEndPoint("example.test", 443), CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Equal(AddressA, calls.Single());
    }

    [Fact]
    public async Task FirstAddressStalls_SecondAddressSucceedsWithoutWaitingForFirst()
    {
        var firstCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var expected = new TrackingStream();
        var connector = CreateConnector(
            new[] { AddressA, AddressB },
            async (address, _, cancellationToken) =>
            {
                if (address.Equals(AddressA))
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        firstCancelled.TrySetResult(true);
                        throw;
                    }
                }

                await Task.Delay(10, cancellationToken);
                return expected;
            },
            TimeSpan.FromMilliseconds(40));

        var sw = Stopwatch.StartNew();
        var result = await connector.ConnectAsync(new DnsEndPoint("example.test", 443), CancellationToken.None);
        sw.Stop();

        Assert.Same(expected, result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"Connection took {sw.ElapsedMilliseconds}ms");
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FirstAddressFailsImmediately_SecondAddressSucceeds()
    {
        using var expected = new TrackingStream();
        var connector = CreateConnector(
            new[] { AddressA, AddressB },
            (address, _, _) => address.Equals(AddressA)
                ? Task.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused))
                : Task.FromResult<Stream>(expected),
            TimeSpan.FromMilliseconds(20));

        var result = await connector.ConnectAsync(new DnsEndPoint("example.test", 443), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task LaterCandidateSucceeds_FirstSuccessWins()
    {
        using var expected = new TrackingStream();
        var connector = CreateConnector(
            new[] { AddressA, AddressB, AddressC },
            async (address, _, cancellationToken) =>
            {
                if (address != AddressC)
                {
                    await Task.Delay(5, cancellationToken);
                    throw new SocketException((int)SocketError.TimedOut);
                }

                return expected;
            },
            TimeSpan.FromMilliseconds(20));

        var result = await connector.ConnectAsync(new DnsEndPoint("example.test", 443), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task AllAddressesFail_ThrowsNetworkFailure()
    {
        var connector = CreateConnector(
            new[] { AddressA, AddressB },
            (address, _, _) => Task.FromException<Stream>(
                new SocketException((int)SocketError.HostUnreachable)),
            TimeSpan.FromMilliseconds(10));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            connector.ConnectAsync(new DnsEndPoint("example.test", 443), CancellationToken.None));

        Assert.Contains("example.test", exception.Message);
        Assert.IsType<SocketException>(exception.InnerException);
    }

    [Fact]
    public async Task CallerCancellation_CancelsOutstandingAttempts()
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = CreateConnector(
            new[] { AddressA, AddressB },
            async (_, _, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }

                throw new InvalidOperationException();
            },
            TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            connector.ConnectAsync(new DnsEndPoint("example.test", 443), cts.Token));

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LosingStreamIsDisposedAfterAnotherCandidateWins()
    {
        using var losing = new TrackingStream();
        using var winner = new TrackingStream();
        var connector = CreateConnector(
            new[] { AddressA, AddressB },
            async (address, _, _) =>
            {
                if (address.Equals(AddressA))
                {
                    await Task.Delay(100);
                    return losing;
                }

                await Task.Delay(10);
                return winner;
            },
            TimeSpan.FromMilliseconds(20));

        var result = await connector.ConnectAsync(new DnsEndPoint("example.test", 443), CancellationToken.None);

        Assert.Same(winner, result);
        Assert.True(losing.WasDisposed);
        Assert.False(winner.WasDisposed);
    }

    private static ResilientConnectionConnector CreateConnector(
        IReadOnlyList<IPAddress> addresses,
        Func<IPAddress, int, CancellationToken, Task<Stream>> connect,
        TimeSpan? stagger = null)
    {
        return new ResilientConnectionConnector(
            new NullLogger(),
            stagger,
            (_, _) => Task.FromResult(addresses.ToArray()),
            connect);
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

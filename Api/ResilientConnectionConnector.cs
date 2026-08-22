using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using BdoClient.Logging;

namespace BdoClient.Api;

/// <summary>
/// Connects a hostname through a small staggered set of DNS candidates so a
/// single unreachable address does not hold up the whole HTTP request.
/// </summary>
internal sealed class ResilientConnectionConnector
{
    private readonly ILogger _logger;
    private readonly TimeSpan _stagger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveAddresses;
    private readonly Func<IPAddress, int, CancellationToken, Task<Stream>> _connect;

    public ResilientConnectionConnector(ILogger logger, TimeSpan? stagger = null)
        : this(
            logger,
            stagger,
            static (host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken),
            ConnectSocketAsync)
    {
    }

    internal ResilientConnectionConnector(
        ILogger logger,
        TimeSpan? stagger,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses,
        Func<IPAddress, int, CancellationToken, Task<Stream>> connect)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stagger = stagger ?? TimeSpan.FromMilliseconds(250);
        if (_stagger < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(stagger));

        _resolveAddresses = resolveAddresses ?? throw new ArgumentNullException(nameof(resolveAddresses));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var dnsSw = Stopwatch.StartNew();
        IPAddress[] addresses;
        try
        {
            addresses = await _resolveAddresses(endpoint.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Debug($"TCP DNS failed host={endpoint.Host} error={ex.GetType().Name}: {ex.Message}");
            throw;
        }

        dnsSw.Stop();
        var candidates = OrderCandidates(addresses);
        if (candidates.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        _logger.Debug($"TCP DNS resolved host={endpoint.Host} addresses={candidates.Count} dns_ms={dnsSw.ElapsedMilliseconds}");

        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var attempts = candidates
            .Select((address, index) => RunAttemptAsync(endpoint, address, index, attemptCts.Token))
            .ToArray();
        var remaining = attempts.Cast<Task>().ToList();
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var results = new List<AttemptResult>(attempts.Length);
        Stream? winner = null;
        AttemptResult? winningAttempt = null;

        try
        {
            while (remaining.Count > 0)
            {
                var waitSet = remaining.Append(cancellationTask).ToArray();
                var completed = await Task.WhenAny(waitSet).ConfigureAwait(false);
                if (completed == cancellationTask)
                {
                    attemptCts.Cancel();
                    await ObserveAttemptsAsync(attempts).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                remaining.Remove(completed);
                var result = await ((Task<AttemptResult>)completed).ConfigureAwait(false);
                results.Add(result);

                if (result.Stream != null)
                {
                    winner = result.Stream;
                    winningAttempt = result;
                    attemptCts.Cancel();
                    break;
                }
            }

            if (winner != null && winningAttempt != null)
            {
                var allResults = await Task.WhenAll(attempts).ConfigureAwait(false);
                DisposeNonWinningStreams(allResults, winner);
                _logger.Debug($"TCP connect winner host={endpoint.Host} address={winningAttempt.Address} family={winningAttempt.Address.AddressFamily} attempts={results.Count} connect_ms={winningAttempt.ElapsedMilliseconds}");
                return winner;
            }
        }
        finally
        {
            if (winner == null)
            {
                attemptCts.Cancel();
                await ObserveAttemptsAsync(attempts).ConfigureAwait(false);
                DisposeNonWinningStreams(results, null);
            }
        }

        var lastError = results.LastOrDefault(result => result.Error != null)?.Error;
        throw new HttpRequestException(
            $"Unable to connect to {endpoint.Host}:{endpoint.Port} after {candidates.Count} address attempts.",
            lastError);
    }

    internal static IReadOnlyList<IPAddress> OrderCandidates(IReadOnlyList<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        var groups = addresses
            .Select((address, index) => (address, index))
            .GroupBy(item => item.address.AddressFamily)
            .OrderBy(group => group.Min(item => item.index))
            .Select(group => group.Select(item => item.address).ToArray())
            .ToArray();

        var ordered = new List<IPAddress>(addresses.Count);
        for (var index = 0; ordered.Count < addresses.Count; index++)
        {
            foreach (var group in groups)
            {
                if (index < group.Length)
                    ordered.Add(group[index]);
            }
        }

        return ordered;
    }

    private async Task<AttemptResult> RunAttemptAsync(
        DnsEndPoint endpoint,
        IPAddress address,
        int index,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (index > 0)
                await Task.Delay(TimeSpan.FromTicks(_stagger.Ticks * index), cancellationToken).ConfigureAwait(false);

            _logger.Debug($"TCP connect candidate host={endpoint.Host} address={address} family={address.AddressFamily} attempt={index + 1}");
            var stream = await _connect(address, endpoint.Port, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new AttemptResult(address, stream, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var category = ex is OperationCanceledException ? "cancelled" : ex.GetType().Name;
            _logger.Debug($"TCP connect candidate failed host={endpoint.Host} address={address} category={category} elapsed_ms={sw.ElapsedMilliseconds}");
            return new AttemptResult(address, null, ex, sw.ElapsedMilliseconds);
        }
    }

    private static async Task ObserveAttemptsAsync(IEnumerable<Task<AttemptResult>> attempts)
    {
        await Task.WhenAll(attempts).ConfigureAwait(false);
    }

    private static void DisposeNonWinningStreams(IEnumerable<AttemptResult> results, Stream? winner)
    {
        foreach (var result in results)
        {
            if (result.Stream != null && !ReferenceEquals(result.Stream, winner))
                result.Stream.Dispose();
        }
    }

    private static async Task<Stream> ConnectSocketAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed record AttemptResult(
        IPAddress Address,
        Stream? Stream,
        Exception? Error,
        long ElapsedMilliseconds);
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BdoClient.Logging;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class GitHubRepositoryBridgeTests
{
    private const string ReleasesJson = "[{\"tag_name\":\"v1.2.8\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-01-01T00:00:00Z\",\"assets\":[]}]";

    [Fact]
    public async Task LegacySuccess_DoesNotQueryFutureRepository()
    {
        var handler = new RoutingHandler(
            (_, _) => Task.FromResult(Response(HttpStatusCode.OK, ReleasesJson)));

        var result = await CreateClient(handler).FetchReleasesAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(handler.Requests);
        Assert.Contains("/repos/merelyigor/bdo-ua-client/releases", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("BDO-UA-Client", handler.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task LegacyNotFound_QueriesFutureRepositoryOnce()
    {
        var handler = new RoutingHandler(
            (_, _) => Task.FromResult(Response(HttpStatusCode.NotFound)),
            (_, _) => Task.FromResult(Response(HttpStatusCode.OK, ReleasesJson)));

        var result = await CreateClient(handler).FetchReleasesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/repos/merelyigor/bdo-ua-client/releases", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("/repos/merelyigor/ua-localization-hub/releases", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task BothRepositoriesNotFound_ReturnsFailureWithoutFurtherAttempts()
    {
        var handler = new RoutingHandler(
            (_, _) => Task.FromResult(Response(HttpStatusCode.NotFound)),
            (_, _) => Task.FromResult(Response(HttpStatusCode.NotFound)));

        var result = await CreateClient(handler).FetchReleasesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("404", result.ErrorMessage!);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task LegacyServerError_DoesNotQueryFutureRepository()
    {
        var handler = new RoutingHandler((_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError)));

        var result = await CreateClient(handler).FetchReleasesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("500", result.ErrorMessage!);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LegacyMalformedJson_DoesNotQueryFutureRepository()
    {
        var handler = new RoutingHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "not json")));

        var result = await CreateClient(handler).FetchReleasesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("JSON", result.ErrorMessage!);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellation_StopsBridgeBeforeAnotherRepositoryAttempt()
    {
        var handler = new RoutingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Response(HttpStatusCode.OK, ReleasesJson);
        });
        var cts = new CancellationTokenSource();

        var fetchTask = CreateClient(handler).FetchReleasesAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
        Assert.Single(handler.Requests);
    }

    private static GitHubUpdateClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), new NullLogger());

    private static HttpResponseMessage Response(HttpStatusCode status, string? body = null)
        => new(status)
        {
            Content = body == null
                ? null
                : new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses;

        public List<HttpRequestMessage> Requests { get; } = new();

        public RoutingHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
            => _responses = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_responses.Count == 0)
                return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable));
            return _responses.Dequeue()(request, cancellationToken);
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

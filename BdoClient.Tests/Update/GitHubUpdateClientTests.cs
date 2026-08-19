using System.Net;
using System.Text;
using BdoClient.Logging;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class GitHubUpdateClientTests
{
    private static GitHubUpdateClient CreateClient(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(response, statusCode);
        var httpClient = new HttpClient(handler);
        return new GitHubUpdateClient(httpClient, new NullLogger());
    }

    private static GitHubUpdateClient CreateClientWithHandler(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new GitHubUpdateClient(httpClient, new NullLogger());
    }

    [Fact]
    public async Task FetchReleasesAsync_ValidList_ReturnsSuccess()
    {
        var json = """[{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[]}]""";
        var client = CreateClient(json);
        var result = await client.FetchReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("v0.1.3", result.Value![0].TagName);
    }

    [Fact]
    public async Task FetchReleasesAsync_ExtraFields_Tolerated()
    {
        var json = """[{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[],"node_id":"abc","url":"https://example.com","extra_field":42}]""";
        var client = CreateClient(json);
        var result = await client.FetchReleasesAsync();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task FetchReleasesAsync_NullPublishedAt_Tolerated()
    {
        var json = """[{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":null,"assets":[]}]""";
        var client = CreateClient(json);
        var result = await client.FetchReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value![0].PublishedAt);
    }

    [Fact]
    public async Task FetchReleasesAsync_HttpError_ReturnsFailure()
    {
        var client = CreateClient("", HttpStatusCode.Forbidden);
        var result = await client.FetchReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("403", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchReleasesAsync_MalformedJson_ReturnsFailure()
    {
        var client = CreateClient("not json");
        var result = await client.FetchReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchReleasesAsync_Cancellation_ReturnsFailure()
    {
        var client = CreateClient("[]");
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await client.FetchReleasesAsync(cts.Token);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task FetchReleasesAsync_NetworkError_ReturnsFailure()
    {
        var handler = new NetworkErrorHttpMessageHandler();
        var client = CreateClientWithHandler(handler);
        var result = await client.FetchReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("Network", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchReleasesAsync_CorrectUrl_PerPage()
    {
        var handler = new RecordingHttpMessageHandler("[]");
        var client = CreateClientWithHandler(handler);
        await client.FetchReleasesAsync();
        Assert.Single(handler.Requests);
        var url = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("api.github.com", url);
        Assert.Contains("/releases", url);
        Assert.Contains("per_page=100", url);
    }

    [Fact]
    public async Task FetchReleasesAsync_CorrectHeaders()
    {
        var handler = new RecordingHttpMessageHandler("[]");
        var client = CreateClientWithHandler(handler);
        await client.FetchReleasesAsync();
        var req = handler.Requests[0];
        Assert.False(req.Headers.Contains("Authorization"));
        Assert.Contains("application/vnd.github+json", string.Join(",", req.Headers.Accept.Select(a => a.ToString())));
        Assert.True(req.Headers.Contains("X-GitHub-Api-Version"));
    }

    [Fact]
    public async Task FetchReleasesAsync_ExactlyOneGetRequest()
    {
        var handler = new RecordingHttpMessageHandler("[]");
        var client = CreateClientWithHandler(handler);
        await client.FetchReleasesAsync();
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string response, HttpStatusCode statusCode)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            });
        }
    }

    private class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHttpMessageHandler(string response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            });
        }
    }

    private class NetworkErrorHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

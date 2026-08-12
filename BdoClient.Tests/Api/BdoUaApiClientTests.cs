using System.Net;
using System.Text;
using BdoClient.Api;
using BdoClient.Logging;

namespace BdoClient.Tests.Api;

public class BdoUaApiClientTests
{
    private readonly ILogger _logger;
    private readonly BdoUaApiClient _client;

    public BdoUaApiClientTests()
    {
        _logger = new NullLogger();
        _client = CreateClientWithResponse(
            """{"success":true,"data":{"modes":[]}}""",
            HttpStatusCode.OK);
    }

    private static BdoUaApiClient CreateClientWithResponse(string response, HttpStatusCode statusCode)
    {
        var handler = new MockHttpMessageHandler(response, statusCode);
        var httpClient = new HttpClient(handler);
        return new BdoUaApiClient(httpClient, new NullLogger());
    }

    [Fact]
    public async Task GetReleasesAsync_Success_ReturnsSuccess()
    {
        var result = await _client.GetReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetReleasesAsync_ServerError_ReturnsFailure()
    {
        var client = CreateClientWithResponse("", HttpStatusCode.InternalServerError);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task GetReleasesAsync_EmptyResponse_ReturnsFailure()
    {
        var client = CreateClientWithResponse("", HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("Empty", result.ErrorMessage);
    }

    [Fact]
    public async Task GetReleasesAsync_MalformedJson_ReturnsFailure()
    {
        var client = CreateClientWithResponse("not json", HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetReleasesAsync_Cancellation_Throws()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await _client.GetReleasesAsync(cts.Token);
        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
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

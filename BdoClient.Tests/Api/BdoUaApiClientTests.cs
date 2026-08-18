using System.Net;
using System.Text;
using BdoClient.Api;
using BdoClient.Logging;

namespace BdoClient.Tests.Api;

public class BdoUaApiClientTests
{
    private readonly NullLogger _logger = new();

    private static BdoUaApiClient CreateClientWithResponse(string response, HttpStatusCode statusCode, int timeoutSeconds = 30)
    {
        var handler = new MockHttpMessageHandler(response, statusCode);
        var httpClient = new HttpClient(handler);
        return new BdoUaApiClient(httpClient, new NullLogger(), timeoutSeconds);
    }

    private static BdoUaApiClient CreateClientThatDelays(int delayMs, HttpStatusCode statusCode = HttpStatusCode.OK, string response = "")
    {
        var handler = new DelayingHttpMessageHandler(delayMs, statusCode, response);
        var httpClient = new HttpClient(handler);
        return new BdoUaApiClient(httpClient, new NullLogger(), timeoutSeconds: 1);
    }

    [Fact]
    public async Task GetReleasesAsync_Success_ReturnsSuccess()
    {
        var client = CreateClientWithResponse(
            """{"success":true,"data":{"modes":[]}}""",
            HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ApiErrorKind.None, result.ErrorKind);
    }

    [Fact]
    public async Task GetReleasesAsync_ServerError_ReturnsHttpError()
    {
        var client = CreateClientWithResponse("", HttpStatusCode.InternalServerError);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Http, result.ErrorKind);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task GetReleasesAsync_EmptyResponse_ReturnsInvalidResponse()
    {
        var client = CreateClientWithResponse("", HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Contains("Empty", result.ErrorMessage);
    }

    [Fact]
    public async Task GetReleasesAsync_MalformedJson_ReturnsInvalidResponse()
    {
        var client = CreateClientWithResponse("not json", HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
    }

    [Fact]
    public async Task GetReleasesAsync_SuccessFalse_ReturnsInvalidResponse()
    {
        var client = CreateClientWithResponse(
            """{"success":false,"data":null}""",
            HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Contains("success=false", result.ErrorMessage);
    }

    [Fact]
    public async Task GetReleasesAsync_DataNull_ReturnsInvalidResponse()
    {
        var client = CreateClientWithResponse(
            """{"success":true,"data":null}""",
            HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Contains("data=null", result.ErrorMessage);
    }

    [Fact]
    public async Task GetReleasesAsync_CurrentNull_ReturnsSuccess()
    {
        var client = CreateClientWithResponse(
            """{"success":true,"data":{"modes":[{"slug":"test","current":null}]}}""",
            HttpStatusCode.OK);
        var result = await client.GetReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetReleasesAsync_UserCancellation_ReturnsCancelled()
    {
        var client = CreateClientWithResponse(
            """{"success":true,"data":{}}""",
            HttpStatusCode.OK);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await client.GetReleasesAsync(cts.Token);
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Cancelled, result.ErrorKind);
    }

    [Fact]
    public async Task GetReleasesAsync_Timeout_ReturnsTimeout()
    {
        var client = CreateClientThatDelays(delayMs: 3000);
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Timeout, result.ErrorKind);
    }

    [Fact]
    public async Task GetReleasesAsync_NetworkError_ReturnsNetwork()
    {
        var handler = new NetworkErrorHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());
        var result = await client.GetReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Network, result.ErrorKind);
    }

    [Fact]
    public async Task GetReleasesAsync_SingleGetRequest_CorrectUrl()
    {
        var handler = new RecordingHttpMessageHandler("""{"success":true,"data":{"modes":[]}}""");
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetReleasesAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("/releases", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetReleasesAsync_TimingDoesNotAddExtraRequests()
    {
        var handler = new RecordingHttpMessageHandler("""{"success":true,"data":{"modes":[]}}""");
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        await client.GetReleasesAsync();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetReleasesAsync_NonAsciiPayload_BytesAreRealPayloadBytes()
    {
        var payload = """{"success":true,"data":{"modes":[]}}""";
        var utf8Bytes = Encoding.UTF8.GetByteCount(payload);
        var logger = new RecordingLogger();
        var handler = new MockHttpMessageHandler(payload, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, logger);

        await client.GetReleasesAsync();

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("API timing:") && l.Contains("bytes="));
        Assert.NotNull(timingLine);
        Assert.Contains($"bytes={utf8Bytes}", timingLine);
    }

    [Fact]
    public async Task GetReleasesAsync_Timeout_TimingLineContainsTotalMsAndError()
    {
        var logger = new RecordingLogger();
        var handler = new DelayingHttpMessageHandler(delayMs: 3000, HttpStatusCode.OK, "");
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, logger, timeoutSeconds: 1);

        await client.GetReleasesAsync();

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("API timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("total_ms=", timingLine);
        Assert.Contains("error=Timeout", timingLine);
    }

    [Fact]
    public async Task GetReleasesAsync_NetworkError_TimingLineContainsTotalMsAndError()
    {
        var logger = new RecordingLogger();
        var handler = new NetworkErrorHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, logger);

        await client.GetReleasesAsync();

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("API timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("total_ms=", timingLine);
        Assert.Contains("error=Network", timingLine);
    }

    [Fact]
    public async Task GetReleasesAsync_HttpError_TimingLineContainsStatusAndError()
    {
        var logger = new RecordingLogger();
        var handler = new MockHttpMessageHandler("", HttpStatusCode.ServiceUnavailable);
        var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, logger);

        await client.GetReleasesAsync();

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("API timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("status=503", timingLine);
        Assert.Contains("error=Http", timingLine);
        Assert.DoesNotContain("error=Http503", timingLine);
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

    private class DelayingHttpMessageHandler : HttpMessageHandler
    {
        private readonly int _delayMs;
        private readonly HttpStatusCode _statusCode;
        private readonly string _response;

        public DelayingHttpMessageHandler(int delayMs, HttpStatusCode statusCode, string response)
        {
            _delayMs = delayMs;
            _statusCode = statusCode;
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delayMs, cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }

    private class NetworkErrorHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
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

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private class RecordingLogger : ILogger
    {
        public List<string> DebugLines { get; } = new();
        public List<string> InfoLines { get; } = new();
        public List<string> WarningLines { get; } = new();
        public List<string> ErrorLines { get; } = new();

        public void Debug(string message) => DebugLines.Add(message);
        public void Info(string message) => InfoLines.Add(message);
        public void Warning(string message) => WarningLines.Add(message);
        public void Error(string message) => ErrorLines.Add(message);
    }
}

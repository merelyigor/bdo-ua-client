using System.Net;
using BdoClient.Api;
using BdoClient.Models;
using BdoClient.Update;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Logging;

namespace BdoClient.Tests.Api;

public class BdoUaHttpClientConfigurationTests
{
    [Fact]
    public void BuildUserAgent_PublicVersion_UsesStableNumericVersion()
    {
        var version = AppVersionInfo.FromRawVersion("1.4.2");

        Assert.Equal("BdoUaClient/1.4.2 (+https://bdo-ua.com.ua)",
            BdoUaHttpClientConfiguration.BuildUserAgent(version));
    }

    [Fact]
    public void BuildUserAgent_DevelopmentVersion_DoesNotExposeRawMetadata()
    {
        var version = AppVersionInfo.FromRawVersion("1.0.0+77133f3ba5be7a");

        var userAgent = BdoUaHttpClientConfiguration.BuildUserAgent(version);

        Assert.Equal("BdoUaClient/DEV (+https://bdo-ua.com.ua)", userAgent);
        Assert.DoesNotContain("77133f3ba5be7a", userAgent);
    }

    [Fact]
    public void BuildUserAgent_PrereleaseVersion_UsesDevelopmentToken()
    {
        var version = AppVersionInfo.FromRawVersion("0.0.0-dev.local");

        Assert.Equal("BdoUaClient/DEV (+https://bdo-ua.com.ua)",
            BdoUaHttpClientConfiguration.BuildUserAgent(version));
    }

    [Fact]
    public void BuildUserAgent_UnknownVersion_StillIdentifiesApplication()
    {
        var version = AppVersionInfo.FromRawVersion("unknown");

        Assert.Equal("BdoUaClient/unknown (+https://bdo-ua.com.ua)",
            BdoUaHttpClientConfiguration.BuildUserAgent(version));
    }

    [Fact]
    public async Task ConfiguredClient_BdoUaApiRequest_InheritsUserAgent()
    {
        var handler = new RecordingHandler("", HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler);
        BdoUaHttpClientConfiguration.Configure(httpClient, AppVersionInfo.FromRawVersion("1.4.2"));
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        await client.GetReleasesAsync();

        Assert.Single(handler.Requests);
        Assert.Equal("BdoUaClient/1.4.2 (+https://bdo-ua.com.ua)",
            handler.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task ConfiguredClient_LocalizationDownload_InheritsUserAgent()
    {
        var payload = new byte[] { 1, 2, 3 };
        var handler = new RecordingHandler(payload);
        using var httpClient = new HttpClient(handler);
        BdoUaHttpClientConfiguration.Configure(httpClient, AppVersionInfo.FromRawVersion("0.0.0-dev.local"));
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "bdo-ua-user-agent-tests", Guid.NewGuid().ToString("N")));
        paths.EnsureDirectories();
        var installer = new LocalizationInstaller(httpClient, paths, new NullLogger(), retryDelaysMs: Array.Empty<int>());

        var result = await installer.DownloadReleaseAsync(new CurrentRelease
        {
            DownloadUrl = "https://bdo-ua.com.ua/download/releases/test",
            SizeBytes = payload.Length,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
            PublicId = "test"
        });

        Assert.True(result.IsSuccess);
        Assert.Single(handler.Requests);
        Assert.Equal("BdoUaClient/DEV (+https://bdo-ua.com.ua)",
            handler.Requests[0].Headers.UserAgent.ToString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly byte[] _payload;

        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler(byte[] payload)
            : this(payload, HttpStatusCode.OK) { }

        public RecordingHandler(string content, HttpStatusCode statusCode)
            : this(System.Text.Encoding.UTF8.GetBytes(content), statusCode) { }

        private RecordingHandler(byte[] payload, HttpStatusCode statusCode)
        {
            _payload = payload;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(_payload)
            });
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

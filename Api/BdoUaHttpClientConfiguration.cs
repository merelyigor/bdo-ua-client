using BdoClient.Update;
using BdoClient.Logging;

namespace BdoClient.Api;

internal static class BdoUaHttpClientConfiguration
{
    private const string WebsiteUrl = "https://bdo-ua.com.ua";

    public static string BuildUserAgent(AppVersionInfo versionInfo)
    {
        ArgumentNullException.ThrowIfNull(versionInfo);

        var productVersion = versionInfo.IsPublicRelease && versionInfo.PublicVersion.HasValue
            ? versionInfo.PublicVersion.Value.ToString()
            : string.Equals(versionInfo.RawVersion, "unknown", StringComparison.Ordinal)
                ? "unknown"
                : "DEV";

        return $"BdoUaClient/{productVersion} (+{WebsiteUrl})";
    }

    public static void Configure(HttpClient httpClient, AppVersionInfo versionInfo)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BuildUserAgent(versionInfo));
    }

    public static HttpClient CreateHttpClient(
        AppVersionInfo versionInfo,
        ILogger logger,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(versionInfo);
        ArgumentNullException.ThrowIfNull(logger);

        var connector = new ResilientConnectionConnector(logger);
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = connector.ConnectAsync
        };
        var httpClient = new HttpClient(handler);
        if (timeout.HasValue)
            httpClient.Timeout = timeout.Value;

        Configure(httpClient, versionInfo);
        return httpClient;
    }
}

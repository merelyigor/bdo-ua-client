using BdoClient.Update;

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
}

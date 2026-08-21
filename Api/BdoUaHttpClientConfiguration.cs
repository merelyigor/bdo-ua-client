using BdoClient.Update;

namespace BdoClient.Api;

internal static class BdoUaHttpClientConfiguration
{
    private const string WebsiteUrl = "https://bdo-ua.com.ua";

    public static string BuildUserAgent(AppVersionInfo versionInfo)
    {
        ArgumentNullException.ThrowIfNull(versionInfo);

        var productVersion = versionInfo.PublicVersion?.ToString()
            ?? (versionInfo.DisplayVersion == "DEV" ? "DEV" : "unknown");

        return $"BdoUaClient/{productVersion} (+{WebsiteUrl})";
    }

    public static void Configure(HttpClient httpClient, AppVersionInfo versionInfo)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BuildUserAgent(versionInfo));
    }
}

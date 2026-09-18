namespace BdoClient;

/// <summary>
/// Compatibility identities that are part of the current update, startup and
/// storage protocols. These values remain legacy-compatible until a later
/// explicitly approved physical identity migration.
/// </summary>
internal static class ApplicationTechnicalIdentity
{
    public const string RepositoryOwner = "merelyigor";
    public const string CanonicalRepositoryName = "ua-localization-hub";
    public const string LegacyRepositoryName = "bdo-ua-client";
    public const string UserAgent = "BDO-UA-Client";
    public const string ExecutableFileName = "BDO-UA-Client.exe";
    public const string AutostartValueName = "BDO-UA-Client";
    public const string LocalAppDataDirectoryName = "BDO-UA-Client";
    public const string SingleInstanceNamePrefix = "BDO-UA-Client";
    public const string LogFilePrefix = "bdo-ua-client";
    public const string ReplacementWorkspaceDirectoryName = ".bdo-ua-client-update";

    public const string CanonicalRepositorySlug = RepositoryOwner + "/" + CanonicalRepositoryName;
    public const string LegacyRepositorySlug = RepositoryOwner + "/" + LegacyRepositoryName;

    public static string BuildReleasesApiUrl(string repositoryName)
        => $"https://api.github.com/repos/{RepositoryOwner}/{repositoryName}/releases?per_page=100";

    public static string BuildPackageFileName(string version)
        => $"BDO-UA-Client-v{version}-win-x64.zip";
}

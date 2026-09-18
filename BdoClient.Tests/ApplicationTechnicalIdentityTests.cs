namespace BdoClient.Tests;

public sealed class ApplicationTechnicalIdentityTests
{
    [Fact]
    public void CurrentCompatibilityIdentitiesRemainUnchanged()
    {
        Assert.Equal("merelyigor/bdo-ua-client", ApplicationTechnicalIdentity.LegacyRepositorySlug);
        Assert.Equal("merelyigor/ua-localization-hub", ApplicationTechnicalIdentity.CanonicalRepositorySlug);
        Assert.Equal("BDO-UA-Client.exe", ApplicationTechnicalIdentity.ExecutableFileName);
        Assert.Equal("BDO-UA-Client", ApplicationTechnicalIdentity.UserAgent);
        Assert.Equal("BDO-UA-Client", ApplicationTechnicalIdentity.AutostartValueName);
        Assert.Equal("BDO-UA-Client", ApplicationTechnicalIdentity.LocalAppDataDirectoryName);
    }

    [Fact]
    public void PackageNameUsesCurrentLegacyContract()
    {
        Assert.Equal(
            "BDO-UA-Client-v1.2.8-win-x64.zip",
            ApplicationTechnicalIdentity.BuildPackageFileName("1.2.8"));
    }

    [Fact]
    public void ReleaseUrlsUseApprovedRepositorySlugs()
    {
        Assert.Equal(
            "https://api.github.com/repos/merelyigor/bdo-ua-client/releases?per_page=100",
            ApplicationTechnicalIdentity.BuildReleasesApiUrl(ApplicationTechnicalIdentity.LegacyRepositoryName));
        Assert.Equal(
            "https://api.github.com/repos/merelyigor/ua-localization-hub/releases?per_page=100",
            ApplicationTechnicalIdentity.BuildReleasesApiUrl(ApplicationTechnicalIdentity.CanonicalRepositoryName));
    }
}

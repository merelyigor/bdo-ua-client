using BdoClient.Logging;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateSelectionPolicyTests
{
    private static readonly ILogger Logger = new NullLogger();

    private static GitHubRelease MakeRelease(string tag, bool draft = false, bool prerelease = false, string? publishedAt = "2026-01-01T00:00:00Z", params (string name, string url)[] assets)
    {
        return new GitHubRelease
        {
            TagName = tag,
            Draft = draft,
            Prerelease = prerelease,
            PublishedAt = publishedAt,
            Assets = assets.Select(a => new GitHubReleaseAsset
            {
                Name = a.name,
                BrowserDownloadUrl = a.url,
                State = "uploaded"
            }).ToList()
        };
    }

    [Fact]
    public void CurrentNotPublic_ReturnsNull()
    {
        var info = AppVersionInfo.FromRawVersion("0.0.0-dev.abcdef");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentNotPublished_ReturnsNull()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.4", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentDraft_ReturnsNull()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", draft: true, assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.4", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentPrerelease_AllowsNewerPrerelease()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", prerelease: true, assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.4", prerelease: true, assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
    }

    [Fact]
    public void CurrentPrerelease_AllowsNewerStable()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", prerelease: true, assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.4", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
    }

    [Fact]
    public void CurrentStable_IgnoresNewerPrerelease()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.4", prerelease: true, assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentStable_AllowsNewerStable()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.4", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
    }

    [Fact]
    public void MultipleCandidates_SelectsHighestNumeric()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.9", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.10", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.8", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.10", candidate!.TagName);
    }

    [Fact]
    public void DraftCandidate_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.1", draft: true, assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void UnpublishedCandidate_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.1", publishedAt: null, assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void MalformedTag_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("not-a-tag", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void HighestCandidateWithoutManifest_FailClosed()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.2", assets: ("BDO-UA-Client-v0.1.2.zip", "https://example.com/zip")),
            MakeRelease("v0.1.1", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void OlderReleasesIgnored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.1", assets: ("release-manifest.json", "https://example.com/manifest")),
            MakeRelease("v0.1.0", assets: ("release-manifest.json", "https://example.com/manifest"))
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

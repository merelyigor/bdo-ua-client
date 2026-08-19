using System.Text.Json;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class GitHubReleaseModelTests
{
    [Fact]
    public void Deserialize_ValidRelease_AllFieldsParsed()
    {
        var json = """{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[{"name":"release-manifest.json","browser_download_url":"https://example.com/manifest","size":1234,"state":"uploaded","digest":"sha256:abc123"}]}""";
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(release);
        Assert.Equal("v0.1.3", release.TagName);
        Assert.False(release.Draft);
        Assert.False(release.Prerelease);
        Assert.NotNull(release.PublishedAt);
        Assert.Equal(2026, release.PublishedAt.Value.Year);
        Assert.Single(release.Assets!);
        Assert.Equal("release-manifest.json", release.Assets![0].Name);
        Assert.Equal("uploaded", release.Assets[0].State);
        Assert.Equal("sha256:abc123", release.Assets[0].Digest);
    }

    [Fact]
    public void Deserialize_ExtraFields_Tolerated()
    {
        var json = """{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[],"node_id":"abc","url":"https://api.github.com/repos/test/test/releases/1","body":"test","name":"Release 0.1.3","extra_unknown":42}""";
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(release);
        Assert.Equal("v0.1.3", release.TagName);
    }

    [Fact]
    public void Deserialize_NullPublishedAt_IsNull()
    {
        var json = """{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":null,"assets":[]}""";
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(release);
        Assert.Null(release.PublishedAt);
    }

    [Fact]
    public void Deserialize_EmptyAssets()
    {
        var json = """{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[]}""";
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(release);
        Assert.Empty(release.Assets!);
    }

    [Fact]
    public void Deserialize_NullAssets()
    {
        var json = """{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":null}""";
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(release);
        Assert.Null(release.Assets);
    }
}

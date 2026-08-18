using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class FeedChangeDetectorTests
{
    [Fact]
    public void HasSemanticChange_BothNull_ReturnsFalse()
    {
        Assert.False(FeedChangeDetector.HasSemanticChange(null, null));
    }

    [Fact]
    public void HasSemanticChange_OldNull_NewPresent_ReturnsTrue()
    {
        var newFeed = CreateFeed(modeA: "id1");
        Assert.True(FeedChangeDetector.HasSemanticChange(null, newFeed));
    }

    [Fact]
    public void HasSemanticChange_OldPresent_NewNull_ReturnsTrue()
    {
        var oldFeed = CreateFeed(modeA: "id1");
        Assert.True(FeedChangeDetector.HasSemanticChange(oldFeed, null));
    }

    [Fact]
    public void HasSemanticChange_UnchangedFeed_ReturnsFalse()
    {
        var feed = CreateFeed(modeA: "id1");
        Assert.False(FeedChangeDetector.HasSemanticChange(feed, feed));
    }

    [Fact]
    public void HasSemanticChange_SameContent_ReturnsFalse()
    {
        var old = CreateFeed(modeA: "id1", modeB: "id2");
        var @new = CreateFeed(modeA: "id1", modeB: "id2");
        Assert.False(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_NewModeAdded_ReturnsTrue()
    {
        var old = CreateFeed(modeA: "id1");
        var @new = CreateFeed(modeA: "id1", modeB: "id2");
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_ModeRemoved_ReturnsTrue()
    {
        var old = CreateFeed(modeA: "id1", modeB: "id2");
        var @new = CreateFeed(modeA: "id1");
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_SameModeNewPublicId_ReturnsTrue()
    {
        var old = CreateFeed(modeA: "id1");
        var @new = CreateFeed(modeA: "id1-updated");
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_OfficialPatchChanged_ReturnsTrue()
    {
        var old = CreateFeed(modeA: "id1", officialPatch: 100);
        var @new = CreateFeed(modeA: "id1", officialPatch: 101);
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_VersionBumped_ReturnsTrue()
    {
        var old = CreateFeedWithVersion(modeA: "id1", version: 1);
        var @new = CreateFeedWithVersion(modeA: "id1", version: 2);
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_CompatibilityChanged_ReturnsTrue()
    {
        var old = CreateFeedWithCompatibility(modeA: "id1", compatible: true);
        var @new = CreateFeedWithCompatibility(modeA: "id1", compatible: false);
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_PublicNameChanged_ReturnsTrue()
    {
        var old = CreateFeedWithPublicName(modeA: ("id1", "Old Name"));
        var @new = CreateFeedWithPublicName(modeA: ("id1", "New Name"));
        Assert.True(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_GeneratedAtChanged_ReturnsFalse()
    {
        var old = CreateFeed(modeA: "id1");
        old.GeneratedAt = "2026-01-01T00:00:00Z";
        var @new = CreateFeed(modeA: "id1");
        @new.GeneratedAt = "2026-01-02T00:00:00Z";
        Assert.False(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    [Fact]
    public void HasSemanticChange_BothDataNull_ReturnsFalse()
    {
        var old = new ReleasesResponse { Success = true, Data = null };
        var @new = new ReleasesResponse { Success = true, Data = null };
        Assert.False(FeedChangeDetector.HasSemanticChange(old, @new));
    }

    private static ReleasesResponse CreateFeed(
        string? modeA = null, string? modeB = null, int officialPatch = 100)
    {
        var modes = new List<LocalizationMode>();
        if (modeA != null)
            modes.Add(CreateMode("full-ukrainian", modeA));
        if (modeB != null)
            modes.Add(CreateMode("english-items", modeB));

        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                OfficialPatch = officialPatch,
                OfficialSourceUrl = "https://example.com/loc",
                Modes = modes
            }
        };
    }

    private static ReleasesResponse CreateFeedWithVersion(
        string? modeA = null, int version = 1)
    {
        var modes = new List<LocalizationMode>();
        if (modeA != null)
            modes.Add(CreateMode("full-ukrainian", modeA, version: version));

        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                OfficialPatch = 100,
                Modes = modes
            }
        };
    }

    private static ReleasesResponse CreateFeedWithCompatibility(
        string? modeA = null, bool compatible = true)
    {
        var modes = new List<LocalizationMode>();
        if (modeA != null)
            modes.Add(CreateMode("full-ukrainian", modeA, compatible: compatible));

        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                OfficialPatch = 100,
                Modes = modes
            }
        };
    }

    private static ReleasesResponse CreateFeedWithPublicName(
        (string id, string name) modeA = default)
    {
        var modes = new List<LocalizationMode>();
        if (modeA.id != null)
            modes.Add(CreateMode("full-ukrainian", modeA.id, publicName: modeA.name));

        return new ReleasesResponse
        {
            Success = true,
            Data = new ReleaseData
            {
                OfficialPatch = 100,
                Modes = modes
            }
        };
    }

    private static LocalizationMode CreateMode(
        string slug, string publicId, int version = 1,
        bool compatible = true, string? publicName = null)
    {
        return new LocalizationMode
        {
            Slug = slug,
            PublicName = publicName ?? slug,
            Current = new CurrentRelease
            {
                PublicId = publicId,
                Version = version,
                Patch = 100,
                CompatibleWithOfficialPatch = compatible,
                DownloadUrl = "https://example.com/download",
                SizeBytes = 1024,
                Sha256 = "abc123",
                PublishedAt = "2026-01-01T00:00:00Z"
            }
        };
    }
}

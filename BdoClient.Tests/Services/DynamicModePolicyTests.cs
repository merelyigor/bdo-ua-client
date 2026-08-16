using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class DynamicModePolicyTests
{
    private static LocalizationMode MakeMode(string slug, string? publicName = null, CurrentRelease? current = null)
    {
        return new LocalizationMode { Slug = slug, PublicName = publicName, Current = current };
    }

    private static CurrentRelease MakeCurrent(string publicId = "01ABC", int version = 1, int patch = 100,
        string? downloadUrl = "https://example.com/release.loc", string? sha256 = "abc123", long sizeBytes = 1000,
        bool compatible = true, string? publishedAt = null)
    {
        return new CurrentRelease
        {
            PublicId = publicId,
            Version = version,
            Patch = patch,
            DownloadUrl = downloadUrl,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            CompatibleWithOfficialPatch = compatible,
            PublishedAt = publishedAt
        };
    }

    // --- GetInstallableModes ---

    [Fact]
    public void GetInstallableModes_FiltersNullCurrent()
    {
        var a = MakeMode("a", current: MakeCurrent());
        var b = MakeMode("b", current: null);
        var c = MakeMode("c", current: MakeCurrent());

        var result = DynamicModePolicy.GetInstallableModes(new List<LocalizationMode> { a, b, c });

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Slug);
        Assert.Equal("c", result[1].Slug);
    }

    [Fact]
    public void GetInstallableModes_FiltersMalformedCurrent()
    {
        var valid = MakeMode("valid", current: MakeCurrent());
        var emptyPublicId = MakeMode("epid", current: MakeCurrent(publicId: ""));
        var emptyDownloadUrl = MakeMode("edurl", current: MakeCurrent(downloadUrl: ""));
        var emptySha = MakeMode("esha", current: MakeCurrent(sha256: ""));
        var zeroSize = MakeMode("zsize", current: MakeCurrent(sizeBytes: 0));

        var result = DynamicModePolicy.GetInstallableModes(
            new List<LocalizationMode> { valid, emptyPublicId, emptyDownloadUrl, emptySha, zeroSize });

        Assert.Single(result);
        Assert.Equal("valid", result[0].Slug);
    }

    [Fact]
    public void GetInstallableModes_PreservesApiOrder()
    {
        var a = MakeMode("a", current: MakeCurrent());
        var b = MakeMode("b", current: MakeCurrent());
        var c = MakeMode("c", current: MakeCurrent());

        var result = DynamicModePolicy.GetInstallableModes(new List<LocalizationMode> { a, b, c });

        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0].Slug);
        Assert.Equal("b", result[1].Slug);
        Assert.Equal("c", result[2].Slug);
    }

    // --- GetDisplayName ---

    [Fact]
    public void GetDisplayName_PublicNameValid_UsesPublicName()
    {
        var mode = MakeMode("full-ukrainian", publicName: "Повна українська");

        var result = DynamicModePolicy.GetDisplayName(mode);

        Assert.Equal("Повна українська", result);
    }

    [Fact]
    public void GetDisplayName_PublicNameNull_FallsBackToSlug()
    {
        var mode = MakeMode("full-ukrainian", publicName: null);

        var result = DynamicModePolicy.GetDisplayName(mode);

        Assert.Equal("full-ukrainian", result);
    }

    [Fact]
    public void GetDisplayName_PublicNameEmpty_FallsBackToSlug()
    {
        var mode = MakeMode("full-ukrainian", publicName: "  ");

        var result = DynamicModePolicy.GetDisplayName(mode);

        Assert.Equal("full-ukrainian", result);
    }

    [Fact]
    public void GetDisplayName_BothNull_ReturnsFallback()
    {
        var mode = new LocalizationMode { Slug = null, PublicName = null };

        var result = DynamicModePolicy.GetDisplayName(mode);

        Assert.Equal("Невідомий режим", result);
    }

    // --- FormatPublishedDate ---

    [Fact]
    public void FormatPublishedDate_ValidIso_ReturnsLocalDate()
    {
        var result = DynamicModePolicy.FormatPublishedDate("2026-08-15T10:00:00Z");

        Assert.NotNull(result);
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4}$", result);
    }

    [Fact]
    public void FormatPublishedDate_Null_ReturnsNull()
    {
        var result = DynamicModePolicy.FormatPublishedDate(null);

        Assert.Null(result);
    }

    [Fact]
    public void FormatPublishedDate_Invalid_ReturnsNull()
    {
        var result = DynamicModePolicy.FormatPublishedDate("not-a-date");

        Assert.Null(result);
    }

    // --- FormatReleaseLine ---

    [Fact]
    public void FormatReleaseLine_ValidRelease_ContainsVersionAndPatch()
    {
        var mode = MakeMode("test", current: MakeCurrent(version: 5, patch: 397));

        var result = DynamicModePolicy.FormatReleaseLine(mode);

        Assert.Contains("v5", result);
        Assert.Contains("patch 397", result);
    }

    // --- ResolveInitialSelection ---

    [Fact]
    public void ResolveInitialSelection_SavedExists_SelectsSaved()
    {
        var modes = new List<LocalizationMode>
        {
            MakeMode("full-ukrainian"),
            MakeMode("english-items")
        };

        var result = DynamicModePolicy.ResolveInitialSelection("english-items", modes);

        Assert.Equal("english-items", result);
    }

    [Fact]
    public void ResolveInitialSelection_SavedAbsent_SelectsFirst()
    {
        var modes = new List<LocalizationMode>
        {
            MakeMode("full-ukrainian"),
            MakeMode("english-items")
        };

        var result = DynamicModePolicy.ResolveInitialSelection("nonexistent", modes);

        Assert.Equal("full-ukrainian", result);
    }

    [Fact]
    public void ResolveInitialSelection_EmptyList_ReturnsNull()
    {
        var result = DynamicModePolicy.ResolveInitialSelection("anything", new List<LocalizationMode>());

        Assert.Null(result);
    }
}

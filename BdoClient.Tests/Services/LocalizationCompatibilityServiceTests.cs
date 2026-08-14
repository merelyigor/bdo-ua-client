using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class LocalizationCompatibilityServiceTests
{
    private readonly LocalizationCompatibilityService _service = new();

    private CurrentRelease CreateCurrent(
        string publicId = "01ABCDEF1234567890ABCDEF",
        bool compatible = true,
        int version = 1,
        int patch = 100)
    {
        return new CurrentRelease
        {
            PublicId = publicId,
            Version = version,
            Patch = patch,
            Filename = "languagedata_en.loc",
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "abc",
            CompatibleWithOfficialPatch = compatible,
            PublishedAt = "2026-01-01T00:00:00Z"
        };
    }

    // --- current=null ---

    [Fact]
    public void CurrentNull_ReturnsBlocked()
    {
        var result = _service.Check(null);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
        Assert.Contains("not available", result.Reason);
    }

    // --- current.PublicId=null ---

    [Fact]
    public void CurrentPublicIdNull_ReturnsBlocked()
    {
        var current = CreateCurrent(publicId: null!);

        var result = _service.Check(current);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
        Assert.Contains("public_id", result.Reason);
    }

    // --- current.PublicId="" ---

    [Fact]
    public void CurrentPublicIdEmpty_ReturnsBlocked()
    {
        var current = CreateCurrent(publicId: "");

        var result = _service.Check(current);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
    }

    // --- current.PublicId="   " ---

    [Fact]
    public void CurrentPublicIdWhitespace_ReturnsBlocked()
    {
        var current = CreateCurrent(publicId: "   ");

        var result = _service.Check(current);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
    }

    // --- compatible=false + valid PublicId ---

    [Fact]
    public void CompatibleFalse_ValidPublicId_ReturnsBlocked()
    {
        var current = CreateCurrent(compatible: false);

        var result = _service.Check(current);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
        Assert.Contains("compatible", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // --- compatible=true + valid PublicId ---

    [Fact]
    public void CompatibleTrue_ValidPublicId_ReturnsAllowed()
    {
        var current = CreateCurrent();

        var result = _service.Check(current);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    // --- version/patch don't affect compatibility ---

    [Fact]
    public void CompatibleTrue_StrangeVersionPatch_ReturnsAllowed()
    {
        var current = CreateCurrent(version: 999, patch: 9999);

        var result = _service.Check(current);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    // --- compatible=false cannot be overridden by version/patch/public_id ---

    [Fact]
    public void CompatibleFalse_CannotBeOverriddenByVersionPatch()
    {
        var current = CreateCurrent(
            publicId: "01ABCDEF1234567890ABCDEF",
            compatible: false,
            version: 1,
            patch: 100);

        var result = _service.Check(current);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
    }
}

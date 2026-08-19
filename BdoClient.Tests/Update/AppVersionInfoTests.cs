using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class AppVersionInfoTests
{
    [Fact]
    public void FromRawVersion_ExactNumeric_IsPublic()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.4");
        Assert.True(info.IsPublicRelease);
        Assert.NotNull(info.PublicVersion);
        Assert.Equal(0, info.PublicVersion.Value.Major);
        Assert.Equal(1, info.PublicVersion.Value.Minor);
        Assert.Equal(4, info.PublicVersion.Value.Build);
        Assert.Equal("v0.1.4", info.DisplayVersion);
        Assert.Equal("0.1.4", info.RawVersion);
    }

    [Fact]
    public void FromRawVersion_VPrefix_NotPublic()
    {
        var info = AppVersionInfo.FromRawVersion("v0.1.4");
        Assert.False(info.IsPublicRelease);
        Assert.Null(info.PublicVersion);
        Assert.Equal("vv0.1.4", info.DisplayVersion);
    }

    [Fact]
    public void FromRawVersion_DevBuild_NotPublic()
    {
        var info = AppVersionInfo.FromRawVersion("0.0.0-dev.abcdef");
        Assert.False(info.IsPublicRelease);
        Assert.Null(info.PublicVersion);
        Assert.Equal("v0.0.0-dev.abcdef", info.DisplayVersion);
    }

    [Fact]
    public void FromRawVersion_WithMetadata_NotPublic()
    {
        var info = AppVersionInfo.FromRawVersion("1.0.0+abcdef");
        Assert.False(info.IsPublicRelease);
        Assert.Null(info.PublicVersion);
        Assert.Equal("v1.0.0+abcdef", info.DisplayVersion);
    }

    [Fact]
    public void FromRawVersion_Unknown_SafeDiagnostic()
    {
        var info = AppVersionInfo.FromRawVersion("unknown");
        Assert.False(info.IsPublicRelease);
        Assert.Null(info.PublicVersion);
        Assert.Equal("версія невідома", info.DisplayVersion);
    }

    [Fact]
    public void FromRawVersion_Malformed_NotPublic()
    {
        var info = AppVersionInfo.FromRawVersion("not-a-version");
        Assert.False(info.IsPublicRelease);
        Assert.Null(info.PublicVersion);
    }
}

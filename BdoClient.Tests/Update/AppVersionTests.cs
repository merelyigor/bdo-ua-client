using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class AppVersionTests
{
    // --- TryParseCoreVersion: strict X.Y.Z only ---

    [Fact]
    public void TryParseCoreVersion_Valid_ReturnsVersion()
    {
        var v = AppVersion.TryParseCoreVersion("0.1.4");
        Assert.NotNull(v);
        Assert.Equal(0, v.Value.Major);
        Assert.Equal(1, v.Value.Minor);
        Assert.Equal(4, v.Value.Build);
    }

    [Fact]
    public void TryParseCoreVersion_Null_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParseCoreVersion(null));
    }

    [Fact]
    public void TryParseCoreVersion_Empty_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParseCoreVersion(""));
    }

    [Fact]
    public void TryParseCoreVersion_VPrefix_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("v0.1.4"));
    }

    [Fact]
    public void TryParseCoreVersion_UpperVPrefix_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("V0.1.4"));
    }

    [Fact]
    public void TryParseCoreVersion_LeadingWhitespace_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion(" 0.1.4"));
    }

    [Fact]
    public void TryParseCoreVersion_TrailingWhitespace_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("0.1.4 "));
    }

    [Fact]
    public void TryParseCoreVersion_LeadingZeroMajor_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("01.1.4"));
    }

    [Fact]
    public void TryParseCoreVersion_LeadingZeroMinor_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("1.01.4"));
    }

    [Fact]
    public void TryParseCoreVersion_LeadingZeroBuild_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("1.2.03"));
    }

    [Fact]
    public void TryParseCoreVersion_DevSuffix_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("1.2.3-dev"));
    }

    [Fact]
    public void TryParseCoreVersion_MetadataSuffix_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("1.0.0+abcdef"));
    }

    [Fact]
    public void TryParseCoreVersion_TwoParts_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("1.2"));
    }

    [Fact]
    public void TryParseCoreVersion_FourParts_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("1.2.3.4"));
    }

    // --- TryParseReleaseTag: strict vX.Y.Z only ---

    [Fact]
    public void TryParseReleaseTag_ValidLowercaseV_ReturnsVersion()
    {
        var v = AppVersion.TryParseReleaseTag("v0.1.4");
        Assert.NotNull(v);
        Assert.Equal(0, v.Value.Major);
        Assert.Equal(1, v.Value.Minor);
        Assert.Equal(4, v.Value.Build);
    }

    [Fact]
    public void TryParseReleaseTag_Null_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParseReleaseTag(null));
    }

    [Fact]
    public void TryParseReleaseTag_BareVersion_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("0.1.4"));
    }

    [Fact]
    public void TryParseReleaseTag_UpperV_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("V0.1.4"));
    }

    [Fact]
    public void TryParseReleaseTag_LeadingWhitespace_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag(" v0.1.4"));
    }

    [Fact]
    public void TryParseReleaseTag_TrailingWhitespace_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v0.1.4 "));
    }

    [Fact]
    public void TryParseReleaseTag_LeadingZeroMajor_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v01.1.4"));
    }

    [Fact]
    public void TryParseReleaseTag_LeadingZeroMinor_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v1.01.4"));
    }

    [Fact]
    public void TryParseReleaseTag_LeadingZeroBuild_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v1.2.03"));
    }

    [Fact]
    public void TryParseReleaseTag_Suffix_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v1.2.3-dev"));
    }

    // --- Comparison ---

    [Fact]
    public void Compare_LessThan_Satisfied()
    {
        var v1 = new AppVersion(0, 1, 9);
        var v2 = new AppVersion(0, 1, 10);
        Assert.True(v1 < v2);
        Assert.True(v1.CompareTo(v2) < 0);
    }

    [Fact]
    public void Compare_MajorVersion()
    {
        var v1 = new AppVersion(1, 9, 9);
        var v2 = new AppVersion(2, 0, 0);
        Assert.True(v1 < v2);
    }

    [Fact]
    public void Compare_Equal()
    {
        var v1 = new AppVersion(0, 1, 3);
        var v2 = new AppVersion(0, 1, 3);
        Assert.True(v1 == v2);
        Assert.False(v1 < v2);
        Assert.False(v1 > v2);
        Assert.True(v1.Equals(v2));
    }

    [Fact]
    public void Compare_GreaterThan()
    {
        var v1 = new AppVersion(2, 0, 0);
        var v2 = new AppVersion(1, 9, 9);
        Assert.True(v1 > v2);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        Assert.Equal("0.1.4", new AppVersion(0, 1, 4).ToString());
    }

    [Fact]
    public void Inequality_Operator()
    {
        var v1 = new AppVersion(0, 1, 3);
        var v2 = new AppVersion(0, 1, 4);
        Assert.True(v1 != v2);
    }

    [Fact]
    public void GetHashCode_Equal_Versions_SameHash()
    {
        var v1 = new AppVersion(1, 2, 3);
        var v2 = new AppVersion(1, 2, 3);
        Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
    }

    // --- Overflow safety ---

    [Fact]
    public void TryParseCoreVersion_IntMaxValue_Valid()
    {
        var v = AppVersion.TryParseCoreVersion($"{int.MaxValue}.{int.MaxValue}.{int.MaxValue}");
        Assert.NotNull(v);
        Assert.Equal(int.MaxValue, v.Value.Major);
    }

    [Fact]
    public void TryParseCoreVersion_MajorOverflow_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("2147483648.0.0"));
    }

    [Fact]
    public void TryParseCoreVersion_MinorOverflow_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("0.2147483648.0"));
    }

    [Fact]
    public void TryParseCoreVersion_BuildOverflow_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("0.0.2147483648"));
    }

    [Fact]
    public void TryParseCoreVersion_HugeComponent_Invalid()
    {
        Assert.Null(AppVersion.TryParseCoreVersion("999999999999999999999999.0.0"));
    }

    [Fact]
    public void TryParseCoreVersion_Overflow_NoException()
    {
        var ex = Record.Exception(() => AppVersion.TryParseCoreVersion("999999999999999999999999.0.0"));
        Assert.Null(ex);
    }

    [Fact]
    public void TryParseReleaseTag_IntMaxValue_Valid()
    {
        var v = AppVersion.TryParseReleaseTag($"v{int.MaxValue}.{int.MaxValue}.{int.MaxValue}");
        Assert.NotNull(v);
        Assert.Equal(int.MaxValue, v.Value.Major);
    }

    [Fact]
    public void TryParseReleaseTag_MajorOverflow_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v2147483648.0.0"));
    }

    [Fact]
    public void TryParseReleaseTag_MinorOverflow_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v0.2147483648.0"));
    }

    [Fact]
    public void TryParseReleaseTag_BuildOverflow_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v0.0.2147483648"));
    }

    [Fact]
    public void TryParseReleaseTag_HugeComponent_Invalid()
    {
        Assert.Null(AppVersion.TryParseReleaseTag("v999999999999999999999999.0.0"));
    }

    [Fact]
    public void TryParseReleaseTag_Overflow_NoException()
    {
        var ex = Record.Exception(() => AppVersion.TryParseReleaseTag("v999999999999999999999999.0.0"));
        Assert.Null(ex);
    }
}

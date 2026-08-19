using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class AppVersionTests
{
    [Fact]
    public void TryParse_ValidVersion_ReturnsVersion()
    {
        var v = AppVersion.TryParse("0.1.3");
        Assert.NotNull(v);
        Assert.Equal(0, v.Value.Major);
        Assert.Equal(1, v.Value.Minor);
        Assert.Equal(3, v.Value.Build);
    }

    [Fact]
    public void TryParse_Null_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParse(null));
    }

    [Fact]
    public void TryParse_Empty_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParse(""));
    }

    [Fact]
    public void TryParse_WithSuffix_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParse("0.1.3-dev"));
        Assert.Null(AppVersion.TryParse("1.2.3+sha"));
        Assert.Null(AppVersion.TryParse("0.0.0-dev.abcdef"));
    }

    [Fact]
    public void TryParse_TwoParts_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParse("0.1"));
    }

    [Fact]
    public void TryParse_FourParts_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParse("0.1.3.4"));
    }

    [Fact]
    public void TryParse_Negative_ReturnsNull()
    {
        Assert.Null(AppVersion.TryParse("-1.0.0"));
    }

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
        Assert.True(v1 >= v2);
        Assert.True(v1 <= v2);
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
        var v = new AppVersion(0, 1, 3);
        Assert.Equal("0.1.3", v.ToString());
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => AppVersion.Parse("invalid"));
    }

    [Fact]
    public void Parse_Valid_ReturnsVersion()
    {
        var v = AppVersion.Parse("1.2.3");
        Assert.Equal(1, v.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(3, v.Build);
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
}

namespace BdoClient.Tests;

public class LocalizationFlagParserTests
{
    [Fact]
    public void Parse_UaFlag_StripsFlagAndKeepsTitle()
    {
        var result = LocalizationFlagParser.Parse("🇺🇦 Повністю українською");

        Assert.Equal("Повністю українською", result.Title);
        Assert.Equal(new[] { "UA" }, result.CountryCodes);
    }

    [Fact]
    public void Parse_UaGbFlags_StripsBothLeadingFlags()
    {
        var result = LocalizationFlagParser.Parse("🇺🇦🇬🇧 Український текст + англійські назви предметів");

        Assert.Equal("Український текст + англійські назви предметів", result.Title);
        Assert.Equal(new[] { "UA", "GB" }, result.CountryCodes);
    }

    [Fact]
    public void Parse_UnknownFlag_ReturnsReadableCountryCode()
    {
        var result = LocalizationFlagParser.Parse("🇵🇱 Polski");

        Assert.Equal("Polski", result.Title);
        Assert.Equal(new[] { "PL" }, result.CountryCodes);
    }

    [Fact]
    public void Parse_NoFlag_PreservesText()
    {
        var result = LocalizationFlagParser.Parse("Українська локалізація");

        Assert.Equal("Українська локалізація", result.Title);
        Assert.Empty(result.CountryCodes);
    }

    [Fact]
    public void Parse_SingleIndicator_DoesNotCorruptText()
    {
        var result = LocalizationFlagParser.Parse("🇺 Повністю українською");

        Assert.Equal("🇺 Повністю українською", result.Title);
        Assert.Empty(result.CountryCodes);
    }

    [Fact]
    public void Parse_WhitespaceAfterFlags_IsRemovedOnlyFromTitleStart()
    {
        var result = LocalizationFlagParser.Parse("🇬🇧   English items");

        Assert.Equal("English items", result.Title);
        Assert.Equal(new[] { "GB" }, result.CountryCodes);
    }
}

using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class GamePathsTests
{
    [Fact]
    public void GetLocalizationFilePath_CombinesAdsDirAndFileName()
    {
        Assert.Equal(
            Path.Combine("C:\\game", "ads", "languagedata_en.loc"),
            GamePaths.GetLocalizationFilePath("C:\\game"));
    }

    [Fact]
    public void Constants_MatchContract()
    {
        Assert.Equal("ads", GamePaths.AdsDirName);
        Assert.Equal("languagedata_en.loc", GamePaths.LocalizationFileName);
    }
}

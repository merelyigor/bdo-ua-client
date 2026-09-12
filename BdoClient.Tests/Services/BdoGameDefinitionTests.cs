using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class BdoGameDefinitionTests
{
    private static BdoGameDefinition Definition => BdoGameDefinition.Default;

    [Fact]
    public void GetLocalizationFilePath_CombinesAdsDirAndFileName()
    {
        Assert.Equal(
            Path.Combine("C:\\game", "ads", "languagedata_en.loc"),
            Definition.GetLocalizationFilePath("C:\\game"));
    }

    [Fact]
    public void Constants_MatchContract()
    {
        Assert.Equal("black-desert-online", Definition.Id);
        Assert.Equal("Black Desert Online", Definition.DisplayName);
        Assert.Equal(582660, Definition.SteamAppId);
        Assert.Equal("ads", Definition.AdsDirectoryName);
        Assert.Equal("languagedata_en.loc", Definition.LocalizationFileName);
        Assert.Equal("ads_files", Definition.AdsFilesName);
        Assert.Equal("Black Desert", Definition.RegistryDisplayNameMarker);
    }

    [Fact]
    public void ValidateGamePath_UsesTheCanonicalLocalizationTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-definition-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Definition.AdsDirectoryName));
            File.WriteAllBytes(Definition.GetLocalizationFilePath(root), Array.Empty<byte>());

            Assert.True(Definition.ValidateGamePath(root));
            Assert.False(Definition.ValidateGamePath(Path.Combine(root, "missing")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadInstalledPatch_UsesTheBdoAdsFilesContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-patch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Definition.GetAdsFilesPath(root), "languagedata_en.loc 401\n");

            Assert.Equal(401, Definition.TryReadInstalledPatch(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class AdsFilesPatchReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"bdo-ads-files-{Guid.NewGuid():N}");

    public AdsFilesPatchReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void TabSeparatedEntry_ReturnsPatch()
    {
        File.WriteAllText(Path.Combine(_root, "ads_files"), "languagedata_de.loc\t387\nlanguagedata_en.loc\t398\n");
        Assert.Equal(398, AdsFilesPatchReader.TryReadPatch(_root));
    }

    [Fact]
    public void WhitespaceSeparatedEntry_ReturnsPatch()
    {
        File.WriteAllText(Path.Combine(_root, "ads_files"), "languagedata_en.loc    398\n");
        Assert.Equal(398, AdsFilesPatchReader.TryReadPatch(_root));
    }

    [Fact]
    public void UnrelatedEntriesIgnored()
    {
        File.WriteAllText(Path.Combine(_root, "ads_files"), "languagedata_de.loc 387\nlanguagedata_sp.loc 392\n");
        Assert.Null(AdsFilesPatchReader.TryReadPatch(_root));
    }

    [Theory]
    [InlineData("languagedata_en.loc 0")]
    [InlineData("languagedata_en.loc nope")]
    public void MalformedPatch_ReturnsUnavailable(string content)
    {
        File.WriteAllText(Path.Combine(_root, "ads_files"), content);
        Assert.Null(AdsFilesPatchReader.TryReadPatch(_root));
    }

    [Fact]
    public void DuplicateEntry_ReturnsUnavailable()
    {
        File.WriteAllText(Path.Combine(_root, "ads_files"), "languagedata_en.loc 397\nlanguagedata_en.loc 398\n");
        Assert.Null(AdsFilesPatchReader.TryReadPatch(_root));
    }

    [Fact]
    public void MissingFile_ReturnsUnavailable()
    {
        Assert.Null(AdsFilesPatchReader.TryReadPatch(_root));
    }
}

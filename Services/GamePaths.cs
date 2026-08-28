namespace BdoClient.Services;

internal static class GamePaths
{
    internal const string AdsDirName = "ads";
    internal const string LocalizationFileName = "languagedata_en.loc";

    internal static string GetLocalizationFilePath(string gameRoot)
        => Path.Combine(gameRoot, AdsDirName, LocalizationFileName);
}

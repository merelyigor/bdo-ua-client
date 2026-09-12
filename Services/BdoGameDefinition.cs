namespace BdoClient.Services;

/// <summary>
/// Explicit Black Desert game boundary. It owns facts that are specific to the
/// current supported game while application workflows consume the boundary
/// instead of rebuilding those facts themselves.
/// </summary>
public sealed class BdoGameDefinition
{
    public static BdoGameDefinition Default { get; } = new();

    public string Id => "black-desert-online";
    public string DisplayName => "Black Desert Online";
    public int SteamAppId => 582660;
    public string AdsDirectoryName => "ads";
    public string LocalizationFileName => "languagedata_en.loc";
    public string AdsFilesName => "ads_files";
    public string RegistryDisplayNameMarker => "Black Desert";
    public string SteamLibraryFoldersFileName => "libraryfolders.vdf";
    public string SteamAppsDirectoryName => "steamapps";
    public string SteamCommonDirectoryName => "common";
    public string SteamAppManifestFileName => $"appmanifest_{SteamAppId}.acf";

    public IReadOnlyList<string> SteamDefaultPaths { get; } = new[]
    {
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam"
    };

    private readonly AdsFilesPatchReader _patchReader;

    public BdoGameDefinition()
    {
        _patchReader = new AdsFilesPatchReader(AdsFilesName, LocalizationFileName);
    }

    public string GetLocalizationFilePath(string gameRoot)
        => Path.Combine(gameRoot, AdsDirectoryName, LocalizationFileName);

    public string GetAdsFilesPath(string gameRoot)
        => Path.Combine(gameRoot, AdsFilesName);

    public bool ValidateGamePath(string gamePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(gamePath);
            return File.Exists(GetLocalizationFilePath(fullPath));
        }
        catch
        {
            return false;
        }
    }

    public bool IsAdsDirectory(string path)
        => path.TrimEnd('\\', '/').EndsWith(AdsDirectoryName, StringComparison.OrdinalIgnoreCase);

    public bool IsRegistryDisplayName(string displayName)
        => displayName.Contains(RegistryDisplayNameMarker, StringComparison.OrdinalIgnoreCase);

    public string? NormalizeApiPathToGameRoot(string expandedPath)
    {
        if (string.IsNullOrWhiteSpace(expandedPath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(expandedPath);

            if (IsAdsDirectory(fullPath))
            {
                var parent = Path.GetDirectoryName(fullPath.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent))
                    return parent;
            }

            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    public int? TryReadInstalledPatch(string? gameRoot)
        => _patchReader.ReadPatch(gameRoot);
}

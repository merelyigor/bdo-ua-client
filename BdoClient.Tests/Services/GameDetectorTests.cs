using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Tests.Services;

public class GameDetectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly ConfigStore _configStore;
    private readonly GameDetector _detector;
    private readonly NullLogger _logger = new();

    public GameDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
        _configStore = new ConfigStore(_paths, _logger);
        _detector = new GameDetector(_configStore, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ValidateGamePath

    [Fact]
    public void ValidateGamePath_ValidPath_ReturnsTrue()
    {
        var gamePath = CreateFakeGamePath();
        Assert.True(GameDetector.ValidateGamePath(gamePath));
    }

    [Fact]
    public void ValidateGamePath_MissingLocalizationFile_ReturnsFalse()
    {
        var gamePath = Path.Combine(_tempDir, "game");
        Directory.CreateDirectory(Path.Combine(gamePath, "ads"));
        Assert.False(GameDetector.ValidateGamePath(gamePath));
    }

    [Fact]
    public void ValidateGamePath_UnicodePath_ReturnsTrue()
    {
        var gamePath = CreateFakeGamePath("Гра");
        Assert.True(GameDetector.ValidateGamePath(gamePath));
    }

    [Fact]
    public void ValidateGamePath_SpacesPath_ReturnsTrue()
    {
        var gamePath = CreateFakeGamePath("Black Desert Online");
        Assert.True(GameDetector.ValidateGamePath(gamePath));
    }

    [Fact]
    public void ValidateGamePath_NonexistentRoot_ReturnsFalse()
    {
        Assert.False(GameDetector.ValidateGamePath(@"C:\Nonexistent\Path"));
    }

    // NormalizeApiPathToGameRoot

    [Fact]
    public void NormalizeApiPathToGameRoot_TrailingAds_ReturnsParent()
    {
        var gameRoot = CreateFakeGamePath();
        var adsPath = Path.Combine(gameRoot, "ads");

        var result = GameDetector.NormalizeApiPathToGameRoot(adsPath);

        Assert.Equal(Path.GetFullPath(gameRoot), result);
    }

    [Fact]
    public void NormalizeApiPathToGameRoot_TrailingAdsSlash_ReturnsParent()
    {
        var gameRoot = CreateFakeGamePath();
        var adsPath = gameRoot + @"\ads\";

        var result = GameDetector.NormalizeApiPathToGameRoot(adsPath);

        Assert.Equal(Path.GetFullPath(gameRoot), result);
    }

    [Fact]
    public void NormalizeApiPathToGameRoot_GameRoot_ReturnsItself()
    {
        var gameRoot = CreateFakeGamePath();

        var result = GameDetector.NormalizeApiPathToGameRoot(gameRoot);

        Assert.Equal(Path.GetFullPath(gameRoot), result);
    }

    [Fact]
    public void NormalizeApiPathToGameRoot_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(GameDetector.NormalizeApiPathToGameRoot(null!));
        Assert.Null(GameDetector.NormalizeApiPathToGameRoot(""));
    }

    [Fact]
    public void NormalizeApiPathToGameRoot_NonexistentPath_StillNormalizes()
    {
        var result = GameDetector.NormalizeApiPathToGameRoot(@"C:\Nonexistent\Path\ads");

        Assert.Equal(@"C:\Nonexistent\Path", result);
    }

    // ParseLibraryFoldersContent

    [Fact]
    public void ParseLibraryFoldersContent_ModernFormat_ReturnsPaths()
    {
        var vdf = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"
            }
        }
        """;

        var result = GameDetector.ParseLibraryFoldersContent(vdf);

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", result[0]);
        Assert.Equal(@"D:\SteamLibrary", result[1]);
    }

    [Fact]
    public void ParseLibraryFoldersContent_ForwardSlashes_Normalizes()
    {
        var vdf = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:/Steam"
            }
        }
        """;

        var result = GameDetector.ParseLibraryFoldersContent(vdf);

        Assert.Single(result);
        Assert.Equal(@"C:\Steam", result[0]);
    }

    [Fact]
    public void ParseLibraryFoldersContent_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(GameDetector.ParseLibraryFoldersContent(""));
        Assert.Empty(GameDetector.ParseLibraryFoldersContent(null!));
    }

    [Fact]
    public void ParseLibraryFoldersContent_NoPaths_ReturnsEmpty()
    {
        var vdf = """
        "libraryfolders"
        {
            "0"
            {
                "appid"		"582660"
            }
        }
        """;

        Assert.Empty(GameDetector.ParseLibraryFoldersContent(vdf));
    }

    [Fact]
    public void ParseLibraryFoldersContent_NonRootedPath_Skipped()
    {
        var vdf = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"relative/path"
            }
        }
        """;

        Assert.Empty(GameDetector.ParseLibraryFoldersContent(vdf));
    }

    [Fact]
    public void ParseLibraryFoldersContent_Malformed_DoesNotCrash()
    {
        var result = GameDetector.ParseLibraryFoldersContent("not valid vdf content {{ }}");
        Assert.Empty(result);
    }

    // ParseAppManifestContent

    [Fact]
    public void ParseAppManifestContent_Valid_ReturnsInstalldir()
    {
        var acf = """
        "AppState"
        {
            "appid"		"582660"
            "installdir"		"Black Desert Online"
        }
        """;

        var result = GameDetector.ParseAppManifestContent(acf);

        Assert.Equal("Black Desert Online", result);
    }

    [Fact]
    public void ParseAppManifestContent_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(GameDetector.ParseAppManifestContent(""));
        Assert.Null(GameDetector.ParseAppManifestContent(null!));
    }

    [Fact]
    public void ParseAppManifestContent_NoInstalldir_ReturnsNull()
    {
        var acf = """
        "AppState"
        {
            "appid"		"582660"
        }
        """;

        Assert.Null(GameDetector.ParseAppManifestContent(acf));
    }

    [Fact]
    public void ParseAppManifestContent_Malformed_ReturnsNull()
    {
        Assert.Null(GameDetector.ParseAppManifestContent("not valid acf content"));
    }

    // ExpandApiPattern

    [Fact]
    public void ExpandApiPattern_WithDrivePlaceholder_Expands()
    {
        var result = GameDetector.ExpandApiPattern("{drive}:\\Games\\BDO\\ads\\", "C:");

        Assert.Equal(@"C:\Games\BDO\ads\", result);
    }

    [Fact]
    public void ExpandApiPattern_ForwardSlashes_Normalizes()
    {
        var result = GameDetector.ExpandApiPattern("{drive}:/Games/BDO/ads/", "D:");

        Assert.Equal(@"D:\Games\BDO\ads\", result);
    }

    [Fact]
    public void ExpandApiPattern_NoDrivePlaceholder_ReturnsNull()
    {
        Assert.Null(GameDetector.ExpandApiPattern("C:\\Games\\BDO\\ads\\", "C:"));
    }

    [Fact]
    public void ExpandApiPattern_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(GameDetector.ExpandApiPattern(null!, "C:"));
        Assert.Null(GameDetector.ExpandApiPattern("", "C:"));
    }

    // DetectionResult

    [Fact]
    public void DetectionResult_Found()
    {
        var result = DetectionResult.Found(@"C:\Game", DetectionSource.Steam);
        Assert.True(result.IsFound);
        Assert.Equal(@"C:\Game", result.GamePath);
        Assert.Equal(DetectionSource.Steam, result.Source);
        Assert.True(result.Persisted);
    }

    [Fact]
    public void DetectionResult_Found_NotPersisted()
    {
        var result = DetectionResult.Found(@"C:\Game", DetectionSource.Registry, persisted: false);
        Assert.True(result.IsFound);
        Assert.False(result.Persisted);
    }

    [Fact]
    public void DetectionResult_NotFound_SourceIsNull()
    {
        var result = DetectionResult.NotFound();
        Assert.False(result.IsFound);
        Assert.Null(result.GamePath);
        Assert.Null(result.Source);
        Assert.False(result.Persisted);
    }

    // Saved config

    [Fact]
    public async Task DetectFromSavedConfig_ValidPath_ReturnsSavedConfig()
    {
        var gamePath = CreateFakeGamePath();
        var config = new Config { GamePath = gamePath };
        await _configStore.SaveAsync(config);

        var result = await _detector.DetectAsync();

        Assert.True(result.IsFound);
        Assert.Equal(DetectionSource.SavedConfig, result.Source);
        Assert.True(result.Persisted);
    }

    [Fact]
    public async Task DetectFromSavedConfig_InvalidPath_SavedConfigNotUsed()
    {
        var config = new Config { GamePath = @"C:\Invalid\Path" };
        await _configStore.SaveAsync(config);

        var result = await _detector.DetectAsync();

        Assert.NotEqual(DetectionSource.SavedConfig, result.Source);
    }

    [Fact]
    public async Task DetectFromSavedConfig_EmptyPath_SavedConfigNotUsed()
    {
        var config = new Config { GamePath = "" };
        await _configStore.SaveAsync(config);

        var result = await _detector.DetectAsync();

        Assert.NotEqual(DetectionSource.SavedConfig, result.Source);
    }

    // Manual path

    [Fact]
    public async Task ManualPath_ValidPath_SavesToConfig()
    {
        var gamePath = CreateFakeGamePath();

        var result = await _detector.ValidateAndSaveManualPathAsync(gamePath);

        Assert.True(result.IsFound);
        Assert.Equal(DetectionSource.Manual, result.Source);
        Assert.True(result.Persisted);

        var config = _configStore.Load();
        Assert.Equal(gamePath, config.Value!.GamePath);
    }

    [Fact]
    public async Task ManualPath_InvalidPath_DoesNotSave()
    {
        var invalidPath = Path.Combine(_tempDir, "invalid");

        var result = await _detector.ValidateAndSaveManualPathAsync(invalidPath);

        Assert.False(result.IsFound);
        Assert.Null(result.Source);
    }

    [Fact]
    public async Task ManualPath_DoesNotOverwriteLastMode()
    {
        var config = new Config { LastMode = "full-ukrainian" };
        await _configStore.SaveAsync(config);

        var gamePath = CreateFakeGamePath();
        await _detector.ValidateAndSaveManualPathAsync(gamePath);

        var loaded = _configStore.Load();
        Assert.Equal("full-ukrainian", loaded.Value!.LastMode);
        Assert.Equal(gamePath, loaded.Value!.GamePath);
    }

    // API pattern: real contract shape — {drive}:\...\Black Desert Online\ads\

    [Fact]
    public void ApiPattern_RealContractShape_ExpandsAndNormalizesToGameRoot()
    {
        var gameRoot = CreateFakeGamePath();
        var rootName = Path.GetPathRoot(gameRoot)!.TrimEnd('\\', '/');

        var relativeFromRoot = gameRoot[(rootName.Length + 1)..];
        var pattern = $@"{{drive}}:\{relativeFromRoot}\ads\";

        var expanded = GameDetector.ExpandApiPattern(pattern, rootName);
        Assert.NotNull(expanded);
        Assert.Contains("ads", expanded);

        var gameRootResult = GameDetector.NormalizeApiPathToGameRoot(expanded);
        Assert.NotNull(gameRootResult);

        var expectedGameRoot = Path.GetFullPath(gameRoot);
        var actualGameRoot = Path.GetFullPath(gameRootResult!);
        Assert.Equal(expectedGameRoot, actualGameRoot);

        Assert.True(GameDetector.ValidateGamePath(gameRootResult!));
    }

    // Cancellation propagation

    [Fact]
    public async Task ManualPath_Cancellation_ThrowsOperationCanceledException()
    {
        var gamePath = CreateFakeGamePath();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _detector.ValidateAndSaveManualPathAsync(gamePath, cts.Token));
    }

    // Persistence: NotFound has null source

    [Fact]
    public void NotFound_HasNullSource()
    {
        var result = DetectionResult.NotFound();
        Assert.Null(result.Source);
    }

    // ResolveManualGameRoot

    [Fact]
    public void ResolveManualGameRoot_ExactRoot_ReturnsFound()
    {
        var gamePath = CreateFakeGamePath();
        var result = GameDetector.ResolveManualGameRoot(gamePath);
        Assert.Equal(ManualResolveStatus.Found, result.Status);
        Assert.Equal(Path.GetFullPath(gamePath), result.GamePath);
    }

    [Fact]
    public void ResolveManualGameRoot_ChildRoot_ReturnsChild()
    {
        var childName = "BlackDesert";
        var childPath = CreateFakeGamePath(childName);
        var result = GameDetector.ResolveManualGameRoot(_tempDir);
        Assert.Equal(ManualResolveStatus.Found, result.Status);
        Assert.Equal(Path.GetFullPath(childPath), result.GamePath);
    }

    [Fact]
    public void ResolveManualGameRoot_NoValidChild_ReturnsNotFound()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "OtherGame"));
        var result = GameDetector.ResolveManualGameRoot(_tempDir);
        Assert.Equal(ManualResolveStatus.NotFound, result.Status);
    }

    [Fact]
    public void ResolveManualGameRoot_MultipleChildren_ReturnsAmbiguous()
    {
        CreateFakeGamePath("Game1");
        CreateFakeGamePath("Game2");
        var result = GameDetector.ResolveManualGameRoot(_tempDir);
        Assert.Equal(ManualResolveStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void ResolveManualGameRoot_EmptyPath_ReturnsNotFound()
    {
        var result = GameDetector.ResolveManualGameRoot("");
        Assert.Equal(ManualResolveStatus.NotFound, result.Status);
    }

    [Fact]
    public void ResolveManualGameRoot_NullPath_ReturnsNotFound()
    {
        var result = GameDetector.ResolveManualGameRoot(null!);
        Assert.Equal(ManualResolveStatus.NotFound, result.Status);
    }

    private string CreateFakeGamePath(string? subDir = null)
    {
        var dirName = subDir ?? "BDO";
        var gamePath = Path.Combine(_tempDir, dirName);
        var adsPath = Path.Combine(gamePath, "ads");
        Directory.CreateDirectory(adsPath);
        File.WriteAllText(Path.Combine(adsPath, "languagedata_en.loc"), "");
        return gamePath;
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

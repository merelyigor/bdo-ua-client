using System.Text.RegularExpressions;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;
using Microsoft.Win32;

namespace BdoClient.Services;

public sealed class GameDetector
{
    private const string LocalizationFile = "languagedata_en.loc";
    private const string AdsDir = "ads";
    private const int BdoAppId = 582660;
    private const string AppManifestFile = "appmanifest_582660.acf";
    private const string LibraryFoldersFile = "libraryfolders.vdf";
    private const string SteamAppsDir = "steamapps";
    private const string CommonDir = "common";

    private static readonly string[] SteamDefaultPaths = new[]
    {
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
        @"D:\Steam",
        @"D:\SteamLibrary",
        @"E:\SteamLibrary"
    };

    private readonly ConfigStore _configStore;
    private readonly ILogger _logger;

    public GameDetector(ConfigStore configStore, ILogger logger)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static bool ValidateGamePath(string gamePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(gamePath);
            var locFile = Path.Combine(fullPath, AdsDir, LocalizationFile);
            return File.Exists(locFile);
        }
        catch
        {
            return false;
        }
    }

    public async Task<DetectionResult> DetectAsync(
        IReadOnlyList<InstallPathPattern>? apiPatterns = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("Starting game detection");

        var savedResult = await DetectFromSavedConfigAsync(cancellationToken).ConfigureAwait(false);
        if (savedResult != null)
        {
            _logger.Info($"Game found from saved config: {savedResult.GamePath}");
            return savedResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var registryResult = DetectFromRegistry();
        if (registryResult != null)
        {
            _logger.Info($"Game found from registry: {registryResult.GamePath}");
            return registryResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var steamResult = DetectFromSteam(cancellationToken);
        if (steamResult != null)
        {
            _logger.Info($"Game found from Steam: {steamResult.GamePath}");
            return steamResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (apiPatterns != null)
        {
            var apiResult = DetectFromApiPatterns(apiPatterns, cancellationToken);
            if (apiResult != null)
            {
                _logger.Info($"Game found from API pattern: {apiResult.GamePath}");
                return apiResult;
            }
        }

        _logger.Info("Game not found");
        return DetectionResult.NotFound();
    }

    public async Task<DetectionResult> ValidateAndSaveManualPathAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        _logger.Debug($"Validating manual path: {gamePath}");

        if (!ValidateGamePath(gamePath))
        {
            _logger.Warning($"Manual path validation failed: {gamePath}");
            return DetectionResult.NotFound();
        }

        var normalizedPath = Path.GetFullPath(gamePath);
        await SaveDetectedPathAsync(normalizedPath, DetectionSource.Manual, cancellationToken).ConfigureAwait(false);

        _logger.Info($"Manual path validated and saved: {normalizedPath}");
        return DetectionResult.Found(normalizedPath, DetectionSource.Manual);
    }

    private async Task<DetectionResult?> DetectFromSavedConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configResult = _configStore.Load();
            if (configResult.Status == FileLoadStatus.Invalid)
            {
                _logger.Warning($"Config load error: {configResult.Error}");
                return null;
            }

            var gamePath = configResult.Value?.GamePath;
            if (string.IsNullOrEmpty(gamePath))
            {
                _logger.Debug("No saved game path");
                return null;
            }

            if (ValidateGamePath(gamePath))
            {
                _logger.Debug($"Saved path validated: {gamePath}");
                return DetectionResult.Found(Path.GetFullPath(gamePath), DetectionSource.SavedConfig);
            }

            _logger.Debug($"Saved path invalid: {gamePath}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error checking saved config: {ex.Message}");
            return null;
        }
    }

    private DetectionResult? DetectFromRegistry()
    {
        try
        {
            return SearchUninstallRegistry(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                ?? SearchUninstallRegistry(
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
                ?? SearchUninstallRegistry(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    RegistryHive.CurrentUser);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Registry detection error: {ex.Message}");
            return null;
        }
    }

    private DetectionResult? SearchUninstallRegistry(string keyPath, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath, false);

            if (key == null) return null;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName, false);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(displayName)) continue;

                    if (!displayName.Contains("Black Desert", StringComparison.OrdinalIgnoreCase) &&
                        !displayName.Contains("BDO", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var installLocation = subKey.GetValue("InstallLocation") as string
                        ?? subKey.GetValue("UninstallString") as string;

                    if (string.IsNullOrEmpty(installLocation)) continue;

                    var path = installLocation.Trim('"').TrimEnd('\\', '/');
                    if (ValidateGamePath(path))
                    {
                        _logger.Debug($"Found via registry: {path} ({displayName})");
                        return DetectionResult.Found(Path.GetFullPath(path), DetectionSource.Registry);
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private DetectionResult? DetectFromSteam(CancellationToken cancellationToken)
    {
        var steamPaths = GetSteamPaths();

        foreach (var steamPath in steamPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(steamPath)) continue;

            var steamAppsPath = Path.Combine(steamPath, SteamAppsDir);
            if (!Directory.Exists(steamAppsPath)) continue;

            var libraryFoldersPath = Path.Combine(steamAppsPath, LibraryFoldersFile);
            var libraries = ParseLibraryFolders(libraryFoldersPath);

            foreach (var library in libraries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var appManifestPath = Path.Combine(library, SteamAppsDir, AppManifestFile);
                var installdir = ParseAppManifest(appManifestPath);

                if (string.IsNullOrEmpty(installdir)) continue;

                var candidate = Path.Combine(library, SteamAppsDir, CommonDir, installdir);
                if (ValidateGamePath(candidate))
                {
                    _logger.Debug($"Found via Steam: {candidate}");
                    return DetectionResult.Found(Path.GetFullPath(candidate), DetectionSource.Steam);
                }
            }
        }

        return null;
    }

    private static List<string> GetSteamPaths()
    {
        var paths = new List<string>();

        foreach (var defaultPath in SteamDefaultPaths)
        {
            if (Directory.Exists(defaultPath))
                paths.Add(defaultPath);
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(steamPath))
            {
                var normalized = steamPath.Replace('/', '\\');
                if (!paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    paths.Add(normalized);
            }
        }
        catch
        {
        }

        return paths;
    }

    private List<string> ParseLibraryFolders(string libraryFoldersPath)
    {
        var libraries = new List<string>();

        if (!File.Exists(libraryFoldersPath))
        {
            _logger.Debug("libraryfolders.vdf not found");
            return libraries;
        }

        try
        {
            var content = File.ReadAllText(libraryFoldersPath);
            return ParseLibraryFoldersContent(content);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error parsing libraryfolders.vdf: {ex.Message}");
        }

        return libraries;
    }

    internal static List<string> ParseLibraryFoldersContent(string vdfContent)
    {
        var libraries = new List<string>();

        if (string.IsNullOrWhiteSpace(vdfContent))
            return libraries;

        var pathPattern = @"\s+""path""\s+""(.+?)""";
        var matches = Regex.Matches(vdfContent, pathPattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            if (match.Groups.Count < 2) continue;

            var path = match.Groups[1].Value;
            path = path.Replace("\\\\", "\\").Replace("/", "\\");

            if (Path.IsPathRooted(path))
            {
                libraries.Add(path);
            }
        }

        return libraries;
    }

    private string? ParseAppManifest(string appManifestPath)
    {
        if (!File.Exists(appManifestPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(appManifestPath);
            return ParseAppManifestContent(content);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error parsing appmanifest: {ex.Message}");
        }

        return null;
    }

    internal static string? ParseAppManifestContent(string acfContent)
    {
        if (string.IsNullOrWhiteSpace(acfContent))
            return null;

        var installdirPattern = @"\s+""installdir""\s+""(.+?)""";
        var match = Regex.Match(acfContent, installdirPattern, RegexOptions.Multiline);

        if (match.Success && match.Groups.Count >= 2)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    internal static string? ExpandApiPattern(string pattern, string drive)
    {
        if (string.IsNullOrEmpty(pattern))
            return null;

        if (!pattern.Contains("{drive}"))
            return null;

        try
        {
            var driveLetter = drive.TrimEnd('\\', '/').TrimEnd(':');
            var expanded = pattern.Replace("{drive}", driveLetter);
            expanded = expanded.Replace("\\\\", "\\").Replace("/", "\\");
            return expanded;
        }
        catch
        {
            return null;
        }
    }

    private DetectionResult? DetectFromApiPatterns(IReadOnlyList<InstallPathPattern> patterns, CancellationToken cancellationToken)
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.Name)
            .ToList();

        foreach (var pattern in patterns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(pattern.Pattern)) continue;

            foreach (var drive in drives)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var expanded = pattern.Pattern.Replace("{drive}", drive.TrimEnd('\\', '/'));
                    expanded = expanded.Replace("\\\\", "\\").Replace("/", "\\");

                    if (ValidateGamePath(expanded))
                    {
                        _logger.Debug($"Found via API pattern: {expanded}");
                        return DetectionResult.Found(Path.GetFullPath(expanded), DetectionSource.ApiPattern);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Pattern error for {drive}: {ex.Message}");
                }
            }
        }

        return null;
    }

    private async Task SaveDetectedPathAsync(string gamePath, DetectionSource source, CancellationToken cancellationToken)
    {
        try
        {
            var configResult = _configStore.Load();
            var config = configResult.Value ?? new Config();

            config.GamePath = gamePath;
            await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            _logger.Debug($"Game path saved: {gamePath} (source: {source})");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save game path: {ex.Message}");
        }
    }
}

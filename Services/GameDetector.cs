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
    private const string AppManifestFile = "appmanifest_582660.acf";
    private const string LibraryFoldersFile = "libraryfolders.vdf";
    private const string SteamAppsDir = "steamapps";
    private const string CommonDir = "common";

    private static readonly string[] SteamDefaultPaths = new[]
    {
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam"
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

        var registryResult = await DetectFromRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (registryResult != null)
        {
            _logger.Info($"Game found from registry: {registryResult.GamePath}");
            return registryResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var steamResult = await DetectFromSteamAsync(cancellationToken).ConfigureAwait(false);
        if (steamResult != null)
        {
            _logger.Info($"Game found from Steam: {steamResult.GamePath}");
            return steamResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (apiPatterns != null && apiPatterns.Count > 0)
        {
            var apiResult = await DetectFromApiPatternsAsync(apiPatterns, cancellationToken).ConfigureAwait(false);
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
        var persisted = await SaveDetectedPathAsync(normalizedPath, DetectionSource.Manual, cancellationToken).ConfigureAwait(false);

        if (!persisted)
        {
            _logger.Warning($"Manual path validated but config save failed: {normalizedPath}");
            return DetectionResult.Found(normalizedPath, DetectionSource.Manual, persisted: false);
        }

        _logger.Info($"Manual path validated and saved: {normalizedPath}");
        return DetectionResult.Found(normalizedPath, DetectionSource.Manual, persisted: true);
    }

    internal static string? NormalizeApiPathToGameRoot(string expandedPath)
    {
        if (string.IsNullOrWhiteSpace(expandedPath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(expandedPath);

            if (fullPath.TrimEnd('\\', '/').EndsWith(AdsDir, StringComparison.OrdinalIgnoreCase))
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

    private Task<DetectionResult?> DetectFromSavedConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configResult = _configStore.Load();
            if (configResult.Status == FileLoadStatus.Invalid)
            {
                _logger.Warning($"Config load error: {configResult.Error}");
                return Task.FromResult<DetectionResult?>(null);
            }

            var gamePath = configResult.Value?.GamePath;
            if (string.IsNullOrEmpty(gamePath))
            {
                _logger.Debug("No saved game path");
                return Task.FromResult<DetectionResult?>(null);
            }

            if (ValidateGamePath(gamePath))
            {
                _logger.Debug($"Saved path validated: {gamePath}");
                return Task.FromResult<DetectionResult?>(
                    DetectionResult.Found(Path.GetFullPath(gamePath), DetectionSource.SavedConfig, persisted: true));
            }

            _logger.Debug($"Saved path invalid: {gamePath}");
            return Task.FromResult<DetectionResult?>(null);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error checking saved config: {ex.Message}");
            return Task.FromResult<DetectionResult?>(null);
        }
    }

    private async Task<DetectionResult?> DetectFromRegistryAsync(CancellationToken cancellationToken)
    {
        var viewPairs = new (RegistryHive hive, RegistryView view)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32)
        };

        foreach (var (hive, view) in viewPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = SearchUninstallRegistry(hive, view);
                if (result != null)
                {
                    var persisted = await SaveDetectedPathAsync(result.GamePath!, DetectionSource.Registry, cancellationToken).ConfigureAwait(false);
                    return DetectionResult.Found(result.GamePath!, DetectionSource.Registry, persisted);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Registry scan error ({hive}/{view}): {ex.Message}");
            }
        }

        return null;
    }

    private DetectionResult? SearchUninstallRegistry(RegistryHive hive, RegistryView view)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(keyPath, false);

            if (key == null)
            {
                _logger.Debug($"Registry key not found: {hive}\\{keyPath} (view={view})");
                return null;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName, false);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(displayName)) continue;

                    if (!IsBlackDesertEntry(displayName))
                    {
                        _logger.Debug($"Skipping non-BDO registry entry: {displayName}");
                        continue;
                    }

                    var installLocation = subKey.GetValue("InstallLocation") as string;
                    if (string.IsNullOrEmpty(installLocation))
                    {
                        _logger.Debug($"No InstallLocation for: {displayName}");
                        continue;
                    }

                    var path = installLocation.Trim('"').TrimEnd('\\', '/');
                    if (ValidateGamePath(path))
                    {
                        _logger.Debug($"Found via registry: {path} ({displayName}, {hive}\\{view})");
                        return DetectionResult.Found(Path.GetFullPath(path), DetectionSource.Registry);
                    }

                    _logger.Debug($"Registry path invalid: {path} ({displayName})");
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error reading registry subkey {subKeyName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Cannot open registry key {hive}\\{keyPath} (view={view}): {ex.Message}");
        }

        return null;
    }

    private static bool IsBlackDesertEntry(string displayName)
    {
        return displayName.Contains("Black Desert", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DetectionResult?> DetectFromSteamAsync(CancellationToken cancellationToken)
    {
        var steamPaths = GetSteamPaths();

        foreach (var steamPath in steamPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
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
                        var fullPath = Path.GetFullPath(candidate);
                        _logger.Debug($"Found via Steam: {fullPath}");
                        var persisted = await SaveDetectedPathAsync(fullPath, DetectionSource.Steam, cancellationToken).ConfigureAwait(false);
                        return DetectionResult.Found(fullPath, DetectionSource.Steam, persisted);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Steam scan error for {steamPath}: {ex.Message}");
            }
        }

        return null;
    }

    private List<string> GetSteamPaths()
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
        catch (Exception ex)
        {
            _logger.Debug($"Cannot read Steam registry path: {ex.Message}");
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

    private async Task<DetectionResult?> DetectFromApiPatternsAsync(IReadOnlyList<InstallPathPattern> patterns, CancellationToken cancellationToken)
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
                    var expanded = ExpandApiPattern(pattern.Pattern, drive);
                    if (expanded == null) continue;

                    var gameRoot = NormalizeApiPathToGameRoot(expanded);
                    if (gameRoot == null)
                    {
                        _logger.Debug($"API pattern normalized to null: {expanded}");
                        continue;
                    }

                    if (ValidateGamePath(gameRoot))
                    {
                        var fullPath = Path.GetFullPath(gameRoot);
                        _logger.Debug($"Found via API pattern: {fullPath}");
                        var persisted = await SaveDetectedPathAsync(fullPath, DetectionSource.ApiPattern, cancellationToken).ConfigureAwait(false);
                        return DetectionResult.Found(fullPath, DetectionSource.ApiPattern, persisted);
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

    private async Task<bool> SaveDetectedPathAsync(string gamePath, DetectionSource source, CancellationToken cancellationToken)
    {
        try
        {
            var configResult = _configStore.Load();
            var config = configResult.Value ?? new Config();

            config.GamePath = gamePath;
            await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            _logger.Debug($"Game path saved: {gamePath} (source: {source})");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to save game path: {gamePath} (source: {source}): {ex.Message}");
            return false;
        }
    }
}

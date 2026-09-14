using System.Text.Json;
using System.Text.Json.Serialization;
using BdoClient.Logging;

namespace BdoClient.Storage;

/// <summary>
/// Moves the historical single-game layout into the canonical BDO scope.
/// This is intentionally the only boundary that knows the legacy layout was
/// BDO-owned; current stores receive only canonical scoped paths.
/// </summary>
public sealed class LegacyBdoPersistenceMigrator
{
    private readonly AppPaths _legacyPaths;
    private readonly GamePersistencePaths _scopedPaths;
    private readonly ILogger _logger;

    public LegacyBdoPersistenceMigrator(
        AppPaths legacyPaths,
        GamePersistencePaths scopedPaths,
        ILogger logger)
    {
        _legacyPaths = legacyPaths ?? throw new ArgumentNullException(nameof(legacyPaths));
        _scopedPaths = scopedPaths ?? throw new ArgumentNullException(nameof(scopedPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void MigrateIfNeeded()
    {
        MigrateLegacyConfigIfNeeded();
        MoveDirectoryIfNeeded(_legacyPaths.StateDir, _scopedPaths.StateDir, "installation state");
        MoveDirectoryIfNeeded(_legacyPaths.BackupsDir, _scopedPaths.BackupsDir, "backups");
    }

    private void MigrateLegacyConfigIfNeeded()
    {
        var source = _legacyPaths.ConfigFile;
        if (!File.Exists(source))
            return;

        var scopedConfigExists = File.Exists(_scopedPaths.ConfigFile)
            || Directory.Exists(_scopedPaths.ConfigFile);
        var applicationConfigPath = _legacyPaths.ApplicationConfigFile;
        var applicationConfigExists = File.Exists(applicationConfigPath)
            || Directory.Exists(applicationConfigPath);

        if (scopedConfigExists && applicationConfigExists)
        {
            _logger.Warning("Legacy config remains alongside canonical game/application config; canonical state remains authoritative.");
            return;
        }

        LegacyConfigDocument? legacyConfig;
        try
        {
            var json = File.ReadAllText(source);
            legacyConfig = JsonSerializer.Deserialize<LegacyConfigDocument>(json, JsonOptions);
            if (legacyConfig is null)
            {
                _logger.Error("Legacy config deserialized to null; preserving source and continuing independent migration.");
                return;
            }
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException
            || ex is JsonException
            || ex is NotSupportedException)
        {
            _logger.Error($"Failed to read legacy config; preserving source and continuing independent migration: {ex.Message}");
            return;
        }

        try
        {
            if (!scopedConfigExists)
            {
                WriteNewFile(
                    _scopedPaths.ConfigFile,
                    JsonSerializer.Serialize(
                        new Config
                        {
                            GamePath = legacyConfig.GamePath,
                            LastMode = legacyConfig.LastMode
                        }, JsonOptions));
            }

            if (!applicationConfigExists)
            {
                WriteNewFile(
                    applicationConfigPath,
                    JsonSerializer.Serialize(
                        new ApplicationConfig
                        {
                            AutostartPromptDismissed = legacyConfig.AutostartPromptDismissed
                        }, JsonOptions));
            }

            File.Delete(source);
            if (File.Exists(source))
                throw new IOException("Legacy config still exists after migration.");

            _logger.Info("Legacy BDO config migrated to canonical game and application scopes.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to migrate legacy config: {ex.Message}");
            throw new InvalidOperationException("Could not migrate legacy config safely.", ex);
        }
    }

    private void MoveDirectoryIfNeeded(string source, string destination, string description)
    {
        if (!Directory.Exists(source))
            return;

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            _logger.Warning($"Legacy {description} and canonical scoped state both exist; canonical state remains authoritative.");
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(source, destination);
            EnsureMoved(source, destination, description);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to migrate legacy {description}: {ex.Message}");
            throw new InvalidOperationException($"Could not migrate legacy {description} safely.", ex);
        }
    }

    private static void EnsureMoved(string source, string destination, string description)
    {
        var destinationExists = File.Exists(destination) || Directory.Exists(destination);
        var sourceExists = File.Exists(source) || Directory.Exists(source);
        if (!destinationExists || sourceExists)
            throw new IOException($"Legacy {description} migration could not be verified.");
    }

    private static void WriteNewFile(string destination, string content)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempFile = destination + $".migration.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempFile, content);
            File.Move(tempFile, destination, overwrite: false);
            if (!File.Exists(destination)
                || !string.Equals(File.ReadAllText(destination), content, StringComparison.Ordinal))
                throw new IOException($"Migration destination was not created: {destination}");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private sealed class LegacyConfigDocument
    {
        [JsonPropertyName("game_path")]
        public string? GamePath { get; set; }

        [JsonPropertyName("last_mode")]
        public string? LastMode { get; set; }

        [JsonPropertyName("autostart_prompt_dismissed")]
        public bool AutostartPromptDismissed { get; set; }
    }
}

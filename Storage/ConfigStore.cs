using System.Text.Json;
using BdoClient.Logging;

namespace BdoClient.Storage;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public ConfigStore(AppPaths paths, ILogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FileLoadResult<Config> Load()
    {
        if (!File.Exists(_paths.ConfigFile))
        {
            _logger.Debug("Config file not found, using defaults");
            return FileLoadResult<Config>.Missing(new Config());
        }

        try
        {
            var json = File.ReadAllText(_paths.ConfigFile);
            var config = JsonSerializer.Deserialize<Config>(json, JsonOptions);

            if (config == null)
            {
                _logger.Warning("Config file deserialized to null");
                return FileLoadResult<Config>.Invalid("Deserialized to null");
            }

            return FileLoadResult<Config>.Valid(config);
        }
        catch (JsonException ex)
        {
            _logger.Error($"Config file is invalid: {ex.Message}");
            return FileLoadResult<Config>.Invalid($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read config file: {ex.Message}");
            return FileLoadResult<Config>.Invalid($"Read error: {ex.Message}");
        }
    }

    public async Task SaveAsync(Config config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempFile = _paths.ConfigFile + ".tmp";

        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_paths.ConfigFile))
            {
                File.Replace(tempFile, _paths.ConfigFile, null);
            }
            else
            {
                File.Move(tempFile, _paths.ConfigFile);
            }

            _logger.Debug("Config saved successfully");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save config: {ex.Message}");
            CleanupTempFile(tempFile);
            throw;
        }
    }

    private void CleanupTempFile(string tempFile)
    {
        try
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup temp file: {ex.Message}");
        }
    }
}

using System.Text.Json;
using BdoClient.Logging;

namespace BdoClient.Storage;

public sealed class ApplicationConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configFile;
    private readonly ILogger _logger;

    public ApplicationConfigStore(AppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _configFile = paths.ApplicationConfigFile;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FileLoadResult<ApplicationConfig> Load()
    {
        if (!File.Exists(_configFile))
            return FileLoadResult<ApplicationConfig>.Missing(new ApplicationConfig());

        try
        {
            var json = File.ReadAllText(_configFile);
            var config = JsonSerializer.Deserialize<ApplicationConfig>(json, JsonOptions);
            return config == null
                ? FileLoadResult<ApplicationConfig>.Invalid("Deserialized to null")
                : FileLoadResult<ApplicationConfig>.Valid(config);
        }
        catch (JsonException ex)
        {
            _logger.Error($"Application config is invalid: {ex.Message}");
            return FileLoadResult<ApplicationConfig>.Invalid($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read application config: {ex.Message}");
            return FileLoadResult<ApplicationConfig>.Invalid($"Read error: {ex.Message}");
        }
    }

    public async Task SaveAsync(
        ApplicationConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempFile = _configFile + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);
            if (File.Exists(_configFile))
                File.Replace(tempFile, _configFile, null);
            else
                File.Move(tempFile, _configFile, overwrite: false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save application config: {ex.Message}");
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch (Exception cleanupEx)
            {
                _logger.Warning($"Failed to cleanup application config temp file: {cleanupEx.Message}");
            }

            throw;
        }
    }
}

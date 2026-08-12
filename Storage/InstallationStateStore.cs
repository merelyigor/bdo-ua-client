using System.Text.Json;
using BdoClient.Logging;

namespace BdoClient.Storage;

public sealed class InstallationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public InstallationStateStore(AppPaths paths, ILogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FileLoadResult<InstallationMetadata> Load()
    {
        if (!File.Exists(_paths.InstallationFile))
        {
            _logger.Debug("Installation metadata not found");
            return FileLoadResult<InstallationMetadata>.Missing();
        }

        try
        {
            var json = File.ReadAllText(_paths.InstallationFile);
            var metadata = JsonSerializer.Deserialize<InstallationMetadata>(json, JsonOptions);

            if (metadata == null)
            {
                _logger.Warning("Installation metadata deserialized to null");
                return FileLoadResult<InstallationMetadata>.Invalid("Deserialized to null");
            }

            return FileLoadResult<InstallationMetadata>.Valid(metadata);
        }
        catch (JsonException ex)
        {
            _logger.Error($"Installation metadata is invalid: {ex.Message}");
            return FileLoadResult<InstallationMetadata>.Invalid($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read installation metadata: {ex.Message}");
            return FileLoadResult<InstallationMetadata>.Invalid($"Read error: {ex.Message}");
        }
    }

    public async Task SaveAsync(InstallationMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        var tempFile = _paths.InstallationFile + ".tmp";

        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_paths.InstallationFile))
            {
                File.Replace(tempFile, _paths.InstallationFile, null);
            }
            else
            {
                File.Move(tempFile, _paths.InstallationFile);
            }

            _logger.Debug("Installation metadata saved successfully");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save installation metadata: {ex.Message}");
            CleanupTempFile(tempFile);
            throw;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_paths.InstallationFile))
            {
                File.Delete(_paths.InstallationFile);
                _logger.Debug("Installation metadata cleared");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to clear installation metadata: {ex.Message}");
            throw;
        }

        await Task.CompletedTask;
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

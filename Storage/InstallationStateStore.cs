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

    // Test seam: called instead of real save. Allows tests to inject cancellation/failure.
    internal Func<InstallationMetadata, CancellationToken, Task>? OnSaveAsync { get; set; }

    internal string StateDir => _paths.StateDir;
    internal string InstallationFile => _paths.InstallationFile;

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

            var validationError = ValidateMetadata(metadata);
            if (validationError != null)
            {
                _logger.Warning($"Installation metadata validation failed: {validationError}");
                return FileLoadResult<InstallationMetadata>.Invalid(validationError);
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

    internal byte[]? CaptureRawState()
    {
        return File.Exists(_paths.InstallationFile)
            ? File.ReadAllBytes(_paths.InstallationFile)
            : null;
    }

    public async Task SaveAsync(InstallationMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // Test seam: delegate save to hook if set
        if (OnSaveAsync != null)
        {
            await OnSaveAsync(metadata, cancellationToken).ConfigureAwait(false);
            return;
        }

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

    private static string? ValidateMetadata(InstallationMetadata metadata)
    {
        if (metadata.InstalledAt == default)
            return "installed_at is default";

        return metadata.Source switch
        {
            InstallationSource.Api => ValidateApiMetadata(metadata),
            InstallationSource.Official => ValidateOfficialMetadata(metadata),
            _ => $"Unknown source: {metadata.Source}"
        };
    }

    private static string? ValidateApiMetadata(InstallationMetadata metadata)
    {
        if (string.IsNullOrEmpty(metadata.ModeSlug))
            return "mode_slug is empty";
        if (string.IsNullOrEmpty(metadata.PublicId))
            return "public_id is empty";
        if (metadata.Version == null)
            return "version is null";
        if (string.IsNullOrEmpty(metadata.Sha256))
            return "sha256 is empty";
        if (metadata.GamePatch == null)
            return "game_patch is null";
        return null;
    }

    private static string? ValidateOfficialMetadata(InstallationMetadata metadata)
    {
        return null;
    }
}

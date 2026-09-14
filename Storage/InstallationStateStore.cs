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

    private readonly string _stateDir;
    private readonly string _installationFile;
    private readonly ILogger _logger;

    // Test seam: called instead of real save. Allows tests to inject cancellation/failure.
    internal Func<InstallationMetadata, CancellationToken, Task>? OnSaveAsync { get; set; }

    internal string StateDir => _stateDir;
    internal string InstallationFile => _installationFile;

    // Legacy-layout adapter is retained only for compatibility fixtures and
    // bounded migration tests; production composition uses GamePersistencePaths.
    internal InstallationStateStore(AppPaths paths, ILogger logger)
        : this(paths?.StateDir ?? throw new ArgumentNullException(nameof(paths)),
            paths.InstallationFile, logger)
    {
    }

    public InstallationStateStore(GamePersistencePaths paths, ILogger logger)
        : this(paths?.StateDir ?? throw new ArgumentNullException(nameof(paths)),
            paths.InstallationFile, logger)
    {
    }

    private InstallationStateStore(string stateDir, string installationFile, ILogger logger)
    {
        _stateDir = stateDir;
        _installationFile = installationFile;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FileLoadResult<InstallationMetadata> Load()
    {
        if (!File.Exists(_installationFile))
        {
            _logger.Debug("Installation metadata not found");
            return FileLoadResult<InstallationMetadata>.Missing();
        }

        try
        {
            var json = File.ReadAllText(_installationFile);
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
        return File.Exists(_installationFile)
            ? File.ReadAllBytes(_installationFile)
            : null;
    }

    internal async Task<bool> RestoreRawStateAsync(
        byte[]? stateBytes, CancellationToken cancellationToken = default)
    {
        if (stateBytes == null)
        {
            if (!File.Exists(_installationFile))
                return true;

            File.Delete(_installationFile);
            return !File.Exists(_installationFile);
        }

        var tempFile = Path.Combine(_stateDir, $"installation.raw-restore.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempFile, stateBytes, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_installationFile))
            {
                File.Replace(tempFile, _installationFile, null);
            }
            else
            {
                File.Move(tempFile, _installationFile, overwrite: false);
            }

            var restoredBytes = await File.ReadAllBytesAsync(_installationFile, cancellationToken)
                .ConfigureAwait(false);
            return restoredBytes.Length == stateBytes.Length
                && restoredBytes.AsSpan().SequenceEqual(stateBytes);
        }
        finally
        {
            CleanupTempFile(tempFile);
        }
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
        var tempFile = _installationFile + ".tmp";

        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_installationFile))
            {
                File.Replace(tempFile, _installationFile, null);
            }
            else
            {
                File.Move(tempFile, _installationFile);
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
            if (File.Exists(_installationFile))
            {
                File.Delete(_installationFile);
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

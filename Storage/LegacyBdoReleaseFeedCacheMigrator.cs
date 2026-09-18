using BdoClient.Logging;

namespace BdoClient.Storage;

/// <summary>
/// Best-effort import of the historical global BDO feed cache into the
/// canonical BDO game scope. The legacy source is retained for compatibility.
/// </summary>
public sealed class LegacyBdoReleaseFeedCacheMigrator
{
    private readonly AppPaths _legacyPaths;
    private readonly GamePersistencePaths _scopedPaths;
    private readonly ILogger _logger;

    public LegacyBdoReleaseFeedCacheMigrator(
        AppPaths legacyPaths,
        GamePersistencePaths scopedPaths,
        ILogger logger)
    {
        _legacyPaths = legacyPaths ?? throw new ArgumentNullException(nameof(legacyPaths));
        _scopedPaths = scopedPaths ?? throw new ArgumentNullException(nameof(scopedPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ImportIfNeeded()
    {
        var source = Path.Combine(_legacyPaths.CacheDir, "release-feed.json");
        var destination = _scopedPaths.ReleaseFeedCacheFile;
        if (!File.Exists(source) || File.Exists(destination) || Directory.Exists(destination))
            return;

        var loaded = ReleaseFeedCacheStore.LoadFile(source, _logger);
        if (loaded.Status != FileLoadStatus.Valid)
        {
            _logger.Warning("Legacy BDO release feed cache was not imported because it is missing or invalid.");
            return;
        }

        var tempFile = destination + $".migration.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(_scopedPaths.CacheDir);
            File.Copy(source, tempFile, overwrite: false);
            File.Move(tempFile, destination, overwrite: false);

            var verified = ReleaseFeedCacheStore.LoadFile(destination, _logger);
            if (verified.Status != FileLoadStatus.Valid)
            {
                _logger.Warning("Legacy BDO release feed cache import could not be verified; scoped cache remains non-authoritative.");
                return;
            }

            _logger.Info("Legacy BDO release feed cache imported into the canonical game scope.");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Legacy BDO release feed cache import failed; continuing without cache: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to clean up legacy release feed cache temp file: {ex.Message}");
            }
        }
    }
}

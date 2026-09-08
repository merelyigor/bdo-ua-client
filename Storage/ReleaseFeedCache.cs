using System.Text.Json;
using System.Text.Json.Serialization;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Storage;

public sealed class ReleaseFeedCacheSnapshot
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("saved_at_utc")]
    public DateTimeOffset SavedAtUtc { get; set; }

    [JsonPropertyName("data")]
    public ReleaseFeedCacheData? Data { get; set; }
}

public sealed class ReleaseFeedCacheData
{
    [JsonPropertyName("official_patch")]
    public int OfficialPatch { get; set; }

    [JsonPropertyName("official_source_url")]
    public string? OfficialSourceUrl { get; set; }

    [JsonPropertyName("modes")]
    public List<ReleaseFeedCacheMode>? Modes { get; set; }
}

public sealed class ReleaseFeedCacheMode
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("public_name")]
    public string? PublicName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("audience")]
    public string? Audience { get; set; }

    [JsonPropertyName("current")]
    public ReleaseFeedCacheCurrent? Current { get; set; }
}

public sealed class ReleaseFeedCacheCurrent
{
    [JsonPropertyName("public_id")]
    public string? PublicId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("patch")]
    public int Patch { get; set; }

    [JsonPropertyName("compatible_with_official_patch")]
    public bool CompatibleWithOfficialPatch { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }
}

internal static class ReleaseFeedCacheMapper
{
    public const int CurrentSchemaVersion = 1;

    public static ReleaseFeedCacheSnapshot FromLiveFeed(
        ReleasesResponse feed,
        DateTimeOffset savedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (feed.Data?.Modes == null)
            throw new InvalidDataException("Release feed modes are missing.");

        var data = new ReleaseFeedCacheData
        {
            OfficialPatch = feed.Data.OfficialPatch,
            OfficialSourceUrl = feed.Data.OfficialSourceUrl,
            Modes = feed.Data.Modes.Select(mode => new ReleaseFeedCacheMode
            {
                Slug = mode.Slug,
                PublicName = mode.PublicName,
                Description = mode.Description,
                Audience = mode.Audience,
                Current = mode.Current == null ? null : new ReleaseFeedCacheCurrent
                {
                    PublicId = mode.Current.PublicId,
                    Version = mode.Current.Version,
                    DownloadUrl = mode.Current.DownloadUrl,
                    SizeBytes = mode.Current.SizeBytes,
                    Sha256 = mode.Current.Sha256,
                    Patch = mode.Current.Patch,
                    CompatibleWithOfficialPatch = mode.Current.CompatibleWithOfficialPatch,
                    PublishedAt = mode.Current.PublishedAt
                }
            }).ToList()
        };

        return new ReleaseFeedCacheSnapshot
        {
            SchemaVersion = CurrentSchemaVersion,
            SavedAtUtc = savedAtUtc.ToUniversalTime(),
            Data = data
        };
    }

    public static bool TryToLiveFeed(
        ReleaseFeedCacheSnapshot snapshot,
        out ReleasesResponse? feed,
        out string? error)
    {
        feed = null;
        error = Validate(snapshot);
        if (error != null)
            return false;

        var data = snapshot.Data!;
        feed = new ReleasesResponse
        {
            Success = true,
            GeneratedAt = snapshot.SavedAtUtc.ToUniversalTime().ToString("O"),
            Data = new ReleaseData
            {
                OfficialPatch = data.OfficialPatch,
                OfficialSourceUrl = data.OfficialSourceUrl,
                // Path hints and release history are intentionally not cached.
                InstallPathPatterns = null,
                Modes = data.Modes!.Select(mode => new LocalizationMode
                {
                    Slug = mode.Slug,
                    PublicName = mode.PublicName,
                    Description = mode.Description,
                    Audience = mode.Audience,
                    History = null,
                    Current = mode.Current == null ? null : new CurrentRelease
                    {
                        PublicId = mode.Current.PublicId,
                        Version = mode.Current.Version,
                        DownloadUrl = mode.Current.DownloadUrl,
                        SizeBytes = mode.Current.SizeBytes,
                        Sha256 = mode.Current.Sha256,
                        Patch = mode.Current.Patch,
                        CompatibleWithOfficialPatch = mode.Current.CompatibleWithOfficialPatch,
                        PublishedAt = mode.Current.PublishedAt
                    }
                }).ToList()
            }
        };

        return true;
    }

    public static string? Validate(ReleaseFeedCacheSnapshot? snapshot)
    {
        if (snapshot == null)
            return "Cache snapshot is null";
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            return $"Unsupported cache schema: {snapshot.SchemaVersion}";
        if (snapshot.SavedAtUtc == default || snapshot.SavedAtUtc.Offset != TimeSpan.Zero)
            return "saved_at_utc must be a UTC timestamp";
        if (snapshot.Data?.Modes == null)
            return "Cache data modes are missing";

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mode in snapshot.Data.Modes)
        {
            if (string.IsNullOrWhiteSpace(mode.Slug))
                return "Cache mode slug is missing";
            if (!slugs.Add(mode.Slug))
                return $"Cache contains duplicate mode slug: {mode.Slug}";

            if (mode.Current == null)
                continue;

            var current = mode.Current;
            if (string.IsNullOrWhiteSpace(current.PublicId))
                return $"Cache current public_id is missing for mode {mode.Slug}";
            if (current.Version <= 0 || current.Patch <= 0 || current.SizeBytes <= 0)
                return $"Cache current numeric identity is invalid for mode {mode.Slug}";
            if (string.IsNullOrWhiteSpace(current.DownloadUrl)
                || !Uri.TryCreate(current.DownloadUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
                return $"Cache current download_url is invalid for mode {mode.Slug}";
            if (!IsSha256(current.Sha256))
                return $"Cache current sha256 is invalid for mode {mode.Slug}";
        }

        return null;
    }

    private static bool IsSha256(string? value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed class ReleaseFeedCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly ILogger _logger;

    public ReleaseFeedCacheStore(AppPaths paths, ILogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string CacheFile => Path.Combine(_paths.CacheDir, "release-feed.json");

    public FileLoadResult<ReleaseFeedCacheSnapshot> Load()
    {
        if (!File.Exists(CacheFile))
            return FileLoadResult<ReleaseFeedCacheSnapshot>.Missing();

        try
        {
            var json = File.ReadAllText(CacheFile);
            var snapshot = JsonSerializer.Deserialize<ReleaseFeedCacheSnapshot>(json, JsonOptions);
            var validationError = ReleaseFeedCacheMapper.Validate(snapshot);
            if (validationError != null)
            {
                _logger.Warning($"Release feed cache validation failed: {validationError}");
                return FileLoadResult<ReleaseFeedCacheSnapshot>.Invalid(validationError);
            }

            return FileLoadResult<ReleaseFeedCacheSnapshot>.Valid(snapshot!);
        }
        catch (JsonException ex)
        {
            _logger.Warning($"Release feed cache is invalid: {ex.Message}");
            return FileLoadResult<ReleaseFeedCacheSnapshot>.Invalid($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to read release feed cache: {ex.Message}");
            return FileLoadResult<ReleaseFeedCacheSnapshot>.Invalid($"Read error: {ex.Message}");
        }
    }

    public async Task<bool> SaveAsync(
        ReleasesResponse feed,
        DateTimeOffset? savedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseFeedCacheSnapshot snapshot;
        try
        {
            snapshot = ReleaseFeedCacheMapper.FromLiveFeed(
                feed, savedAtUtc ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Release feed cache was not written: {ex.Message}");
            return false;
        }

        var validationError = ReleaseFeedCacheMapper.Validate(snapshot);
        if (validationError != null)
        {
            _logger.Warning($"Release feed cache was not written: {validationError}");
            return false;
        }

        var tempFile = Path.Combine(
            _paths.CacheDir, $"release-feed.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_paths.CacheDir);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempFile, json, cancellationToken)
                .ConfigureAwait(false);

            if (File.Exists(CacheFile))
                File.Replace(tempFile, CacheFile, null);
            else
                File.Move(tempFile, CacheFile, overwrite: false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to write release feed cache: {ex.Message}");
            CleanupTempFile(tempFile);
            return false;
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
            _logger.Warning($"Failed to cleanup release feed cache temp file: {ex.Message}");
        }
    }
}

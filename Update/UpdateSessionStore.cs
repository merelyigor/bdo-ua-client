using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BdoClient.Logging;
using BdoClient.Storage;

namespace BdoClient.Update;

public enum UpdateSessionLoadStatus
{
    Missing,
    Valid,
    Invalid
}

public sealed class UpdateSessionLoadResult
{
    public UpdateSessionLoadStatus Status { get; }
    public UpdateSession? Session { get; }

    private UpdateSessionLoadResult(UpdateSessionLoadStatus status, UpdateSession? session)
    {
        Status = status;
        Session = session;
    }

    public static UpdateSessionLoadResult Missing => new(UpdateSessionLoadStatus.Missing, null);
    public static UpdateSessionLoadResult Valid(UpdateSession session) => new(UpdateSessionLoadStatus.Valid, session);
    public static UpdateSessionLoadResult Invalid => new(UpdateSessionLoadStatus.Invalid, null);
}

public sealed class UpdateSessionStore
{
    private const string SessionFileName = "update-session.json";
    private const string GuidDRegexPattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string Sha256HexPattern = @"^[0-9a-fA-F]{64}$";

    private static readonly Regex GuidDRegex = new(GuidDRegexPattern, RegexOptions.Compiled);
    private static readonly Regex Sha256HexRegex = new(Sha256HexPattern, RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;

    public UpdateSessionStore(AppPaths appPaths, ILogger logger)
    {
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string NormalizeSessionId(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var guid))
            throw new ArgumentException("Invalid session ID format", nameof(sessionId));
        return guid.ToString("D");
    }

    public static bool IsValidSessionId(string sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && GuidDRegex.IsMatch(sessionId);
    }

    public static bool IsValidSha256Hex(string? value)
    {
        return !string.IsNullOrEmpty(value) && Sha256HexRegex.IsMatch(value);
    }

    public string GetSessionDir(string sessionId)
    {
        var normalized = NormalizeSessionId(sessionId);
        var dir = Path.Combine(_appPaths.UpdatesDir, normalized);
        var normalizedDir = Path.GetFullPath(dir);
        var normalizedRoot = Path.GetFullPath(_appPaths.UpdatesDir);

        if (!normalizedDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Session path escapes updates directory", nameof(sessionId));

        return normalizedDir;
    }

    public UpdatePackageResult WriteSession(UpdateSession session)
    {
        try
        {
            var normalizedId = NormalizeSessionId(session.SessionId);
            var sessionDir = GetSessionDir(normalizedId);
            Directory.CreateDirectory(sessionDir);

            var filePath = Path.Combine(sessionDir, SessionFileName);
            var tempPath = filePath + ".tmp";

            var json = JsonSerializer.Serialize(session, JsonOptions);
            File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);

            File.Move(tempPath, filePath, overwrite: true);

            _logger.Debug($"Session {normalizedId}: written to {filePath}");
            return UpdatePackageResult.Success(session);
        }
        catch (Exception ex)
        {
            _logger.Error($"Session write failed: {ex.Message}");
            return UpdatePackageResult.Failure(UpdatePackageError.SessionWriteFailed, ex.Message);
        }
    }

    public UpdateSessionLoadResult LoadSession(string sessionId)
    {
        try
        {
            var normalizedId = NormalizeSessionId(sessionId);
            var sessionDir = GetSessionDir(normalizedId);
            var filePath = Path.Combine(sessionDir, SessionFileName);

            if (!File.Exists(filePath))
                return UpdateSessionLoadResult.Missing;

            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var session = JsonSerializer.Deserialize<UpdateSession>(json, JsonOptions);

            if (session == null)
                return UpdateSessionLoadResult.Invalid;

            return ValidateSession(session, normalizedId);
        }
        catch (JsonException ex)
        {
            _logger.Warning($"Session JSON parse failed for {sessionId}: {ex.Message}");
            return UpdateSessionLoadResult.Invalid;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session read failed for {sessionId}: {ex.Message}");
            return UpdateSessionLoadResult.Invalid;
        }
    }

    private static UpdateSessionLoadResult ValidateSession(UpdateSession session, string expectedId)
    {
        if (session.SchemaVersion != 1)
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSessionId(session.SessionId))
            return UpdateSessionLoadResult.Invalid;

        var normalizedStoredId = NormalizeSessionId(session.SessionId);
        if (!string.Equals(normalizedStoredId, expectedId, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (!string.Equals(session.State, "staged", StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (session.CreatedAt == default)
            return UpdateSessionLoadResult.Invalid;

        var currentVersion = AppVersion.TryParseCoreVersion(session.CurrentVersion);
        if (!currentVersion.HasValue)
            return UpdateSessionLoadResult.Invalid;

        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        if (!targetVersion.HasValue)
            return UpdateSessionLoadResult.Invalid;

        if (targetVersion.Value <= currentVersion.Value)
            return UpdateSessionLoadResult.Invalid;

        var expectedTag = $"v{targetVersion.Value}";
        if (!string.Equals(session.TargetTag, expectedTag, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (string.IsNullOrWhiteSpace(session.TargetPath) || !Path.IsPathRooted(session.TargetPath))
            return UpdateSessionLoadResult.Invalid;

        if (session.ParentPid <= 0)
            return UpdateSessionLoadResult.Invalid;

        var expectedAssetName = $"BDO-UA-Client-v{targetVersion.Value}-win-x64.zip";
        if (!string.Equals(session.PackageAssetName, expectedAssetName, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSha256Hex(session.PackageSha256))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSha256Hex(session.StagedExeSha256))
            return UpdateSessionLoadResult.Invalid;

        return UpdateSessionLoadResult.Valid(session);
    }

    public void CleanupSession(string sessionId)
    {
        try
        {
            var normalizedId = NormalizeSessionId(sessionId);
            var sessionDir = GetSessionDir(normalizedId);
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, recursive: true);
                _logger.Debug($"Session {normalizedId}: cleaned up");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session cleanup failed for {sessionId}: {ex.Message}");
        }
    }
}

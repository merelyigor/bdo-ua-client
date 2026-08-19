using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Storage;

namespace BdoClient.Update;

public sealed class UpdateSessionStore
{
    private const string SessionFileName = "update-session.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;

    public UpdateSessionStore(AppPaths appPaths, ILogger logger)
    {
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetSessionDir(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _))
            throw new ArgumentException("Invalid session ID format", nameof(sessionId));

        var dir = Path.Combine(_appPaths.UpdatesDir, sessionId);
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
            var sessionDir = GetSessionDir(session.SessionId);
            Directory.CreateDirectory(sessionDir);

            var filePath = Path.Combine(sessionDir, SessionFileName);
            var tempPath = filePath + ".tmp";

            var json = JsonSerializer.Serialize(session, JsonOptions);
            File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);

            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);

            _logger.Debug($"Session {session.SessionId}: written to {filePath}");
            return UpdatePackageResult.Success(session);
        }
        catch (Exception ex)
        {
            _logger.Error($"Session write failed: {ex.Message}");
            return UpdatePackageResult.Failure(UpdatePackageError.SessionWriteFailed, ex.Message);
        }
    }

    public UpdateSession? TryReadSession(string sessionId)
    {
        try
        {
            var sessionDir = GetSessionDir(sessionId);
            var filePath = Path.Combine(sessionDir, SessionFileName);

            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<UpdateSession>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session read failed for {sessionId}: {ex.Message}");
            return null;
        }
    }

    public void CleanupSession(string sessionId)
    {
        try
        {
            var sessionDir = GetSessionDir(sessionId);
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, recursive: true);
                _logger.Debug($"Session {sessionId}: cleaned up");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session cleanup failed for {sessionId}: {ex.Message}");
        }
    }
}

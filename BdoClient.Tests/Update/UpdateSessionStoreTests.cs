using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateSessionStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly UpdateSessionStore _store;

    public UpdateSessionStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        _appPaths = new AppPaths(_tempRoot);
        _appPaths.EnsureDirectories();
        _store = new UpdateSessionStore(_appPaths, new NullLogger());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void GetSessionDir_ValidGuid_ReturnsCorrectPath()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var dir = _store.GetSessionDir(sessionId);
        Assert.StartsWith(_appPaths.UpdatesDir, dir);
    }

    [Fact]
    public void GetSessionDir_InvalidGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.GetSessionDir("not-a-guid"));
    }

    [Fact]
    public void GetSessionDir_PathEscape_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.GetSessionDir("../escape"));
    }

    [Fact]
    public void WriteSession_ThenLoad_Valid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
        Assert.NotNull(result.Session);
        Assert.Equal(session.TargetVersion, result.Session!.TargetVersion);
    }

    [Fact]
    public void WriteSession_Atomic_NoTempAfterSuccess()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.Empty(Directory.GetFiles(sessionDir, "*.tmp"));
    }

    [Fact]
    public void LoadSession_Missing_ReturnsMissing()
    {
        var result = _store.LoadSession(Guid.NewGuid().ToString("D"));
        Assert.Equal(UpdateSessionLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void LoadSession_MalformedJson_ReturnsInvalid()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var dir = _store.GetSessionDir(sessionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "update-session.json"), "not json");
        var result = _store.LoadSession(sessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_WrongSchemaVersion_ReturnsInvalid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var dir = _store.GetSessionDir(session.SessionId);
        var json = File.ReadAllText(Path.Combine(dir, "update-session.json"));
        json = json.Replace("\"schema_version\": 1", "\"schema_version\": 99");
        File.WriteAllText(Path.Combine(dir, "update-session.json"), json);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_WrongSessionId_ReturnsInvalid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var otherId = Guid.NewGuid().ToString("D");
        var result = _store.LoadSession(otherId);
        Assert.Equal(UpdateSessionLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void CleanupSession_RemovesDirectory()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.True(Directory.Exists(sessionDir));
        _store.CleanupSession(session.SessionId);
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void CleanupSession_Missing_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => _store.CleanupSession(Guid.NewGuid().ToString("D"))));
    }

    [Fact]
    public void UpdatesDir_CreatedByEnsureDirectories()
    {
        Assert.True(Directory.Exists(_appPaths.UpdatesDir));
    }

    [Fact]
    public void NormalizeSessionId_BraceFormat_NormalizesToD()
    {
        var guid = Guid.NewGuid();
        var brace = guid.ToString("B");
        var normalized = UpdateSessionStore.NormalizeSessionId(brace);
        Assert.Equal(guid.ToString("D"), normalized);
    }

    [Fact]
    public void WriteSession_SnakeCaseJson()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var dir = _store.GetSessionDir(session.SessionId);
        var json = File.ReadAllText(Path.Combine(dir, "update-session.json"));
        Assert.Contains("schema_version", json);
        Assert.Contains("session_id", json);
        Assert.Contains("target_version", json);
        Assert.DoesNotContain("SchemaVersion", json);
    }

    [Fact]
    public void WriteSession_OverwriteExisting_Atomic()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        session.StagedExeSha256 = new string('c', 64);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
        Assert.Equal(new string('c', 64), result.Session!.StagedExeSha256);
    }

    private static UpdateSession MakeSession() => new()
    {
        SchemaVersion = 1,
        SessionId = Guid.NewGuid().ToString("D"),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        State = "staged",
        CurrentVersion = "0.1.3",
        TargetVersion = "0.1.4",
        TargetTag = "v0.1.4",
        TargetPath = @"C:\test\BDO-UA-Client.exe",
        ParentPid = 12345,
        PackageAssetName = "BDO-UA-Client-v0.1.4-win-x64.zip",
        PackageSha256 = new string('a', 64),
        StagedExeSha256 = new string('b', 64)
    };

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

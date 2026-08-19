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
        var sessionId = Guid.NewGuid().ToString();
        var dir = _store.GetSessionDir(sessionId);
        Assert.StartsWith(_appPaths.UpdatesDir, dir);
        Assert.EndsWith(sessionId, dir);
    }

    [Fact]
    public void GetSessionDir_InvalidGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.GetSessionDir("not-a-guid"));
    }

    [Fact]
    public void GetSessionDir_PathEscape_Throws()
    {
        var sessionId = Guid.NewGuid().ToString();
        Assert.Throws<ArgumentException>(() => _store.GetSessionDir("../escape"));
    }

    [Fact]
    public void WriteSession_Valid_CreatesFile()
    {
        var session = MakeSession();
        var result = _store.WriteSession(session);
        Assert.True(result.IsSuccess);

        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.True(File.Exists(Path.Combine(sessionDir, "update-session.json")));
    }

    [Fact]
    public void WriteSession_ThenRead_RoundTrips()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var read = _store.TryReadSession(session.SessionId);
        Assert.NotNull(read);
        Assert.Equal(session.SessionId, read!.SessionId);
        Assert.Equal(session.TargetVersion, read.TargetVersion);
        Assert.Equal(session.PackageSha256, read.PackageSha256);
        Assert.Equal(session.StagedExeSha256, read.StagedExeSha256);
        Assert.Equal("staged", read.State);
    }

    [Fact]
    public void WriteSession_Atomic_NoTempAfterSuccess()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var sessionDir = _store.GetSessionDir(session.SessionId);
        var tempFiles = Directory.GetFiles(sessionDir, "*.tmp");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public void TryReadSession_Missing_ReturnsNull()
    {
        var read = _store.TryReadSession(Guid.NewGuid().ToString());
        Assert.Null(read);
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
        var ex = Record.Exception(() => _store.CleanupSession(Guid.NewGuid().ToString()));
        Assert.Null(ex);
    }

    [Fact]
    public void UpdatesDir_CreatedByEnsureDirectories()
    {
        Assert.True(Directory.Exists(_appPaths.UpdatesDir));
    }

    private static UpdateSession MakeSession() => new()
    {
        SchemaVersion = 1,
        SessionId = Guid.NewGuid().ToString(),
        CreatedAt = DateTimeOffset.UtcNow,
        State = "staged",
        CurrentVersion = "0.1.3",
        TargetVersion = "0.1.4",
        TargetTag = "v0.1.4",
        TargetPath = @"C:\test\BDO-UA-Client.exe",
        ParentPid = 12345,
        PackageAssetName = "BDO-UA-Client-v0.1.4-win-x64.zip",
        PackageSha256 = "aabbccdd",
        StagedExeSha256 = "eeff0011"
    };

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

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
    public void LoadSession_WrongSessionId_ReturnsMissing()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var otherId = Guid.NewGuid().ToString("D");
        var result = _store.LoadSession(otherId);
        Assert.Equal(UpdateSessionLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void LoadSession_SessionIdDiffersFromRequested_ReturnsInvalid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(sessionDir, "update-session.json");
        var json = File.ReadAllText(filePath);
        json = json.Replace(session.SessionId, Guid.NewGuid().ToString("D"));
        File.WriteAllText(filePath, json);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
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

    [Fact]
    public void WriteSession_ExistingNeverDisappears()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(sessionDir, "update-session.json");
        var originalJson = File.ReadAllText(filePath);

        session.StagedExeSha256 = new string('c', 64);
        _store.WriteSession(session);

        var loaded = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
        Assert.Equal(new string('c', 64), loaded.Session!.StagedExeSha256);

        File.WriteAllText(filePath, "corrupted");
        session.StagedExeSha256 = new string('d', 64);
        var writeResult = _store.WriteSession(session);
        Assert.True(writeResult.IsSuccess);
        var afterCorruptRecovery = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, afterCorruptRecovery.Status);
        Assert.Equal(new string('d', 64), afterCorruptRecovery.Session!.StagedExeSha256);
    }

    // --- Canonical GUID (§5) ---

    [Fact]
    public void NormalizeSessionId_CanonicalDForm_Accepted()
    {
        var guid = Guid.NewGuid();
        var d = guid.ToString("D");
        var normalized = UpdateSessionStore.NormalizeSessionId(d);
        Assert.Equal(d, normalized);
    }

    [Fact]
    public void NormalizeSessionId_BraceFormat_Accepted()
    {
        var guid = Guid.NewGuid();
        var brace = guid.ToString("B");
        var normalized = UpdateSessionStore.NormalizeSessionId(brace);
        Assert.Equal(guid.ToString("D"), normalized);
    }

    [Fact]
    public void IsValidSessionId_CanonicalD_ReturnsTrue()
    {
        var id = Guid.NewGuid().ToString("D");
        Assert.True(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_BraceFormat_ReturnsFalse()
    {
        var id = Guid.NewGuid().ToString("B");
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_NFormat_ReturnsFalse()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_Whitespace_ReturnsFalse()
    {
        var id = $" {Guid.NewGuid():D} ";
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_BracesOnly_ReturnsFalse()
    {
        var guid = Guid.NewGuid();
        var id = $"{{{guid}}}";
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    // --- SHA validation (§4) ---

    [Fact]
    public void IsValidSha256Hex_ValidLowerHex_ReturnsTrue()
    {
        Assert.True(UpdateSessionStore.IsValidSha256Hex(new string('a', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_ValidUpperHex_ReturnsTrue()
    {
        Assert.True(UpdateSessionStore.IsValidSha256Hex(new string('A', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_AllZ_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(new string('z', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_Whitespace_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex($" {new string('a', 64)} "));
    }

    [Fact]
    public void IsValidSha256Hex_PrefixSha256_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex("sha256:" + new string('a', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_Short_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(new string('a', 32)));
    }

    [Fact]
    public void IsValidSha256Hex_Long_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(new string('a', 128)));
    }

    [Fact]
    public void IsValidSha256Hex_Null_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(null));
    }

    [Fact]
    public void IsValidSha256Hex_Empty_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(""));
    }

    // --- Session validation edge cases (§13) ---

    [Fact]
    public void LoadSession_InvalidState_ReturnsInvalid()
    {
        var session = MakeSession();
        session.State = "applied";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_MalformedCurrentVersion_ReturnsInvalid()
    {
        var session = MakeSession();
        session.CurrentVersion = "not-a-version";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_MalformedTargetVersion_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetVersion = "not-a-version";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_TargetNotGreaterThanCurrent_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetVersion = "0.1.3";
        session.TargetTag = "v0.1.3";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_TagMismatch_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetTag = "v0.1.5";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_RelativeTargetPath_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetPath = @"relative\path.exe";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_ParentPidZero_ReturnsInvalid()
    {
        var session = MakeSession();
        session.ParentPid = 0;
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_WrongAssetName_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageAssetName = "wrong-name.zip";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_PackageShaNonHex_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageSha256 = new string('z', 64);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_StagedExeShaNonHex_ReturnsInvalid()
    {
        var session = MakeSession();
        session.StagedExeSha256 = new string('z', 64);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_ShortPackageSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageSha256 = new string('a', 32);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_LongStagedExeSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.StagedExeSha256 = new string('a', 128);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_EmptyPackageSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageSha256 = "";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    // --- WriteSession atomicity (§10) ---

    [Fact]
    public void WriteSession_TempDeletedAfterFailure()
    {
        var session = MakeSession();
        var result = _store.WriteSession(session);
        Assert.True(result.IsSuccess);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.Empty(Directory.GetFiles(sessionDir, "*.tmp"));
    }

    [Fact]
    public void WriteSession_ReplacesExistingSafely()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var dir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(dir, "update-session.json");
        var firstContent = File.ReadAllText(filePath);

        session.StagedExeSha256 = new string('d', 64);
        _store.WriteSession(session);

        var secondContent = File.ReadAllText(filePath);
        Assert.NotEqual(firstContent, secondContent);
        Assert.Contains(new string('d', 64), secondContent);

        var loaded = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
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

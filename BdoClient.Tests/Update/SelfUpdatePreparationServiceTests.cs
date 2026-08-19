using System.Text;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class SelfUpdatePreparationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly NullLogger _logger = new();
    private readonly UpdateSessionStore _store;

    public SelfUpdatePreparationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        _appPaths = new AppPaths(_tempRoot);
        _appPaths.EnsureDirectories();
        _store = new UpdateSessionStore(_appPaths, _logger);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task PrepareAsync_SessionMissing_ReturnsSessionInvalid()
    {
        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(Guid.NewGuid().ToString("D"));
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.SessionInvalid, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_SessionNotStaged_ReturnsSessionInvalid()
    {
        var session = MakeSession();
        session.State = "prepared";
        _store.WriteSession(session);

        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.SessionInvalid, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_StagedExeMissing_ReturnsStagedExeMissing()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.StagedExeMissing, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_HashMismatch_ReturnsHashMismatch()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        // Create staged EXE with wrong hash
        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(stagedExePath, "wrong content");

        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.HashMismatch, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_TargetMissing_ReturnsTargetMissing()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        // Create staged EXE with correct hash
        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(stagedExePath, "staged content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);
        _store.WriteSession(session);

        // Target doesn't exist
        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.TargetMissing, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_Success_CreatesCandidateAndMarksPrepared()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        // Create staged EXE
        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(stagedExePath, "new version");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);
        _store.WriteSession(session);

        // Create target
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old version");

        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Session);
        Assert.NotNull(result.CandidatePath);
        Assert.NotNull(result.OriginalExeSha256);

        // Candidate should exist
        Assert.True(File.Exists(result.CandidatePath));
        var candidateContent = File.ReadAllText(result.CandidatePath);
        Assert.Equal("new version", candidateContent);

        // Session should be marked prepared
        Assert.Equal(UpdateSession.StatePrepared, result.Session.State);
        Assert.NotNull(result.Session.OriginalExeSha256);

        // Verify loaded session
        var loaded = _store.LoadSessionForState(session.SessionId, UpdateSession.StatePrepared);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
    }

    [Fact]
    public async Task PrepareAsync_CandidateInTargetDir()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(stagedExePath, "new");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);
        _store.WriteSession(session);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old");

        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);

        Assert.True(result.IsSuccess);
        // Candidate should be in the same directory as target
        Assert.Equal(targetDir, Path.GetDirectoryName(result.CandidatePath));
    }

    [Fact]
    public async Task PrepareAsync_OriginalExeSha256_MatchesTarget()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(stagedExePath, "new");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);
        _store.WriteSession(session);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "original content");
        var originalSha = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var service = new SelfUpdatePreparationService(_store, _logger);
        var result = await service.PrepareAsync(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalSha, result.OriginalExeSha256);
    }

    // --- Helpers ---

    private static UpdateSession MakeSession() => new()
    {
        SchemaVersion = 1,
        SessionId = Guid.NewGuid().ToString("D"),
        CreatedAt = DateTimeOffset.UtcNow,
        State = "staged",
        CurrentVersion = "0.1.3",
        TargetVersion = "0.1.4",
        TargetTag = "v0.1.4",
        TargetPath = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}", "target", "BDO-UA-Client.exe"),
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

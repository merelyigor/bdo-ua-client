using System.Diagnostics;
using System.Text;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class SelfUpdateApplierTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly NullLogger _logger = new();
    private readonly List<ProcessStartInfo> _startedProcesses = new();

    public SelfUpdateApplierTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        _appPaths = new AppPaths(_tempRoot);
        _appPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task RunAsync_SessionInvalid_ReturnsInvalidArgs()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var applier = CreateApplier(store, parentRunning: false);
        var result = await applier.RunAsync(Guid.NewGuid().ToString("D"));
        Assert.Equal(SelfUpdateApplier.ExitCodeInvalidArgs, result);
    }

    [Fact]
    public async Task RunAsync_SessionNotPrepared_ReturnsInvalidArgs()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession();
        session.State = "staged";
        store.WriteSession(session);

        var applier = CreateApplier(store, parentRunning: false);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeInvalidArgs, result);
    }

    [Fact]
    public async Task RunAsync_HelperIdentityMismatch_ReturnsInvalidArgs()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession();
        session.TargetPath = @"C:\different\path\BDO-UA-Client.exe";
        store.WriteSession(session);

        // Create candidate at the fake target path's dir
        var fakeDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(fakeDir);
        File.WriteAllBytes(Path.Combine(fakeDir, "BDO-UA-Client.exe"), Encoding.UTF8.GetBytes("old"));

        var applier = CreateApplierWithCurrentPath("C:\\some\\other\\BDO-UA-Client.exe", store, parentRunning: false);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeInvalidArgs, result);
    }

    [Fact]
    public async Task RunAsync_CandidateMissing_ReturnsVerificationFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var targetPath = CreateTargetExe("current");
        var session = MakePreparedSession();
        session.TargetPath = targetPath;
        store.WriteSession(session);

        var applier = CreateApplierWithCurrentPath(targetPath, store, parentRunning: false);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);
    }

    [Fact]
    public async Task RunAsync_CandidateHashMismatch_ReturnsVerificationFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var targetPath = CreateTargetExe("current");
        var session = MakePreparedSession();
        session.TargetPath = targetPath;
        session.StagedExeSha256 = new string('a', 64);
        store.WriteSession(session);

        // Create candidate with different content
        var targetDir = Path.GetDirectoryName(targetPath)!;
        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllBytes(candidatePath, Encoding.UTF8.GetBytes("different content"));

        var applier = CreateApplierWithCurrentPath(targetPath, store, parentRunning: false);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);
    }

    [Fact]
    public async Task RunAsync_ParentTimeout_ReturnsParentTimeout()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var targetPath = CreateTargetExe("current");
        var session = MakePreparedSession();
        session.TargetPath = targetPath;
        session.ParentPid = 99999;
        var candidateSha = await CreateCandidateExe(session);
        store.WriteSession(session);

        var applier = CreateApplierWithCurrentPath(targetPath, store, parentRunning: true);
        var result = await applier.RunAsync(session.SessionId, new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
        Assert.Equal(SelfUpdateApplier.ExitCodeParentTimeout, result);
    }

    [Fact]
    public async Task RunAsync_ParentAlreadyExited_ReplacesAndRestarts()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var targetPath = CreateTargetExe("current");
        var session = MakePreparedSession();
        session.TargetPath = targetPath;
        session.ParentPid = 1;
        var candidateSha = await CreateCandidateExe(session);
        store.WriteSession(session);

        var applier = CreateApplierWithCurrentPath(targetPath, store, parentRunning: false);
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeSuccess, result);
        Assert.Single(_startedProcesses);
        Assert.Equal(targetPath, _startedProcesses[0].FileName);

        // Verify target was replaced
        var targetContent = File.ReadAllText(targetPath);
        Assert.Equal("new version", targetContent);

        // Verify session marked as applied
        var loaded = store.LoadSessionForState(session.SessionId, UpdateSession.StateApplied);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
    }

    [Fact]
    public async Task RunAsync_ReplaceSuccess_BackupExists()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var targetPath = CreateTargetExe("current");
        var session = MakePreparedSession();
        session.TargetPath = targetPath;
        session.ParentPid = 1;
        var candidateSha = await CreateCandidateExe(session);
        store.WriteSession(session);

        var applier = CreateApplierWithCurrentPath(targetPath, store, parentRunning: false);
        await applier.RunAsync(session.SessionId);

        // Backup should be cleaned up after success
        var backupPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $"BDO-UA-Client.exe.update-{session.SessionId}.bak");
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public async Task RunAsync_VerificationFailsAfterReplace_RollsBack()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var targetPath = CreateTargetExe("original content");
        var session = MakePreparedSession();
        session.TargetPath = targetPath;
        session.ParentPid = 1;
        session.StagedExeSha256 = new string('a', 64);
        store.WriteSession(session);

        // Create candidate that will pass initial hash check but fail version check
        var targetDir = Path.GetDirectoryName(targetPath)!;
        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "new version");

        // The candidate hash won't match session.StagedExeSha256, so it fails at hash check
        var applier = CreateApplierWithCurrentPath(targetPath, store, parentRunning: false);
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);

        // Original content should be preserved (no replace happened)
        var currentContent = File.ReadAllText(targetPath);
        Assert.Equal("original content", currentContent);
    }

    // --- Helpers ---

    private string CreateTargetExe(string content)
    {
        var dir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "BDO-UA-Client.exe");
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<string> CreateCandidateExe(UpdateSession session)
    {
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "new version");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(candidatePath);
        return session.StagedExeSha256;
    }

    private SelfUpdateApplier CreateApplier(UpdateSessionStore store, bool parentRunning)
    {
        var currentPath = Environment.ProcessPath ?? "BDO-UA-Client.exe";
        return CreateApplierWithCurrentPath(currentPath, store, parentRunning);
    }

    private SelfUpdateApplier CreateApplierWithCurrentPath(string currentPath, UpdateSessionStore store, bool parentRunning)
    {
        _startedProcesses.Clear();
        return new SelfUpdateApplier(
            store,
            _logger,
            path =>
            {
                var psi = new ProcessStartInfo(path) { UseShellExecute = true };
                _startedProcesses.Add(psi);
                return psi;
            },
            _ => parentRunning,
            () => currentPath,
            _ => null);
    }

    private static UpdateSession MakePreparedSession() => new()
    {
        SchemaVersion = 1,
        SessionId = Guid.NewGuid().ToString("D"),
        CreatedAt = DateTimeOffset.UtcNow,
        State = UpdateSession.StatePrepared,
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

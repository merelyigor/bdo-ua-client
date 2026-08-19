using System.Diagnostics;
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
        var applier = CreateApplier(store, "C:\\any.exe");
        var result = await applier.RunAsync(Guid.NewGuid().ToString("D"));
        Assert.Equal(SelfUpdateApplier.ExitCodeInvalidArgs, result);
    }

    [Fact]
    public async Task RunAsync_HelperPathMismatch_ReturnsInvalidArgs()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        store.WriteSession(session);

        var applier = CreateApplier(store, "C:\\different.exe");
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeInvalidArgs, result);
    }

    [Fact]
    public async Task RunAsync_HelperShaMismatch_ReturnsVerificationFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
        store.WriteSession(session);

        var applier = CreateApplier(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);
    }

    [Fact]
    public async Task RunAsync_CandidateMissing_ReturnsVerificationFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);
        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(session.TargetPath)!);
        File.WriteAllText(session.TargetPath, "target content");
        store.WriteSession(session);

        var applier = CreateApplier(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);
    }

    [Fact]
    public async Task RunAsync_CandidateHashMismatch_ReturnsVerificationFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "target content");

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "wrong candidate");

        store.WriteSession(session);
        var applier = CreateApplier(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);
    }

    [Fact]
    public async Task RunAsync_ParentTimeout_ReturnsParentTimeout()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);
        session.ParentPid = 99999;

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "helper content");

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "helper content");

        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);
        store.WriteSession(session);
        var applier = CreateApplier(store, helperPath, parentRunning: true);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await applier.RunAsync(session.SessionId, cts.Token);
        Assert.Equal(SelfUpdateApplier.ExitCodeParentTimeout, result);
    }

    [Fact]
    public async Task RunAsync_BackupAlreadyExists_ReturnsReplaceFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old target content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "helper content");
        var backupPath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.bak");
        File.WriteAllText(backupPath, "existing backup");

        store.WriteSession(session);
        var applier = CreateApplier(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);
        Assert.Equal(SelfUpdateApplier.ExitCodeReplaceFailed, result);

        Assert.True(File.Exists(backupPath));
        Assert.Equal("existing backup", File.ReadAllText(backupPath));
    }

    [Fact]
    public async Task RunAsync_ReplacedFilePreservesOriginalContent_WhenTargetUnchanged()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "helper content");

        store.WriteSession(session);
        var applier = CreateApplier(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeRestartFailed, result);
        Assert.Equal("old content", File.ReadAllText(session.TargetPath));
    }

    [Fact]
    public async Task RunAsync_RestartReturnsNull_RollsBackAndReturnsRestartFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "helper content");

        store.WriteSession(session);
        var applier = CreateApplierWithNullRestart(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeRestartFailed, result);
        Assert.Equal("old content", File.ReadAllText(session.TargetPath));
    }

    [Fact]
    public async Task RunAsync_SuccessfulRestart_ReturnsSuccessAndNewContent()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "new version content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old version content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "new version content");

        store.WriteSession(session);

        ProcessStartInfo? capturedPsi = null;
        var dummyProcess = Process.GetCurrentProcess();
        var applier = new SelfUpdateApplier(
            store,
            _logger,
            () => helperPath,
            path => MakeVersionInfoForPath(path, helperPath),
            _ => false,
            psi => { capturedPsi = psi; _startedProcesses.Add(psi); return dummyProcess; });

        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeSuccess, result);
        Assert.Equal("new version content", File.ReadAllText(session.TargetPath));

        var backupPath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.bak");
        Assert.True(File.Exists(backupPath));
        Assert.Equal("old version content", File.ReadAllText(backupPath));

        var reloadedResult = store.LoadSessionForState(session.SessionId, UpdateSession.StateApplied);
        Assert.Equal(UpdateSessionLoadStatus.Valid, reloadedResult.Status);
        Assert.NotNull(reloadedResult.Session);

        Assert.NotNull(capturedPsi);
        Assert.Equal(session.TargetPath, capturedPsi!.FileName);
        Assert.False(capturedPsi.UseShellExecute);
        Assert.Equal(targetDir, capturedPsi.WorkingDirectory);
        Assert.Empty(capturedPsi.ArgumentList);
    }

    [Fact]
    public async Task RunAsync_PostReplaceTargetVerificationFails_RollsBack()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "new version content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old version content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "new version content");

        store.WriteSession(session);

        var postReplaceVersionOverride = false;
        var applier = new SelfUpdateApplier(
            store,
            _logger,
            () => helperPath,
            path =>
            {
                if (postReplaceVersionOverride && File.Exists(path) &&
                    string.Equals(Path.GetFullPath(path), Path.GetFullPath(session.TargetPath), StringComparison.OrdinalIgnoreCase))
                {
                    var fakeInfo = FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location);
                    typeof(FileVersionInfo).GetField("_fileVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fakeInfo, "99.99.99.0");
                    typeof(FileVersionInfo).GetField("_productVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fakeInfo, "99.99.99");
                    return fakeInfo;
                }
                return MakeVersionInfoForPath(path, helperPath);
            },
            _ => false,
            psi => { _startedProcesses.Add(psi); return null; });

        var originalTargetSha = session.OriginalExeSha256;

        File.WriteAllText(helperPath, "interference");
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeVerificationFailed, result);
        Assert.True(File.Exists(session.TargetPath));
        var restoredSha = await HashHelper.ComputeFileSha256Async(session.TargetPath);
        Assert.Equal(originalTargetSha, restoredSha);
    }

    [Fact]
    public async Task RunAsync_RestartThrows_RollsBackAndReturnsRestartFailed()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "helper content");

        store.WriteSession(session);
        var applier = CreateApplierWithThrowingRestart(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeRestartFailed, result);
        Assert.Equal("old content", File.ReadAllText(session.TargetPath));

        var backupPath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.bak");
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public async Task RunAsync_FailedNewCollision_SkipsRollback()
    {
        var store = new UpdateSessionStore(_appPaths, _logger);
        var session = MakePreparedSession(store);
        var stagedDir = store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(stagedDir, "BDO-UA-Client.exe");
        File.WriteAllText(helperPath, "helper content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(helperPath);

        session.TargetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old content");
        session.OriginalExeSha256 = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        var candidatePath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.new");
        File.WriteAllText(candidatePath, "helper content");

        var failedNewPath = Path.Combine(targetDir, $"BDO-UA-Client.exe.update-{session.SessionId}.failed-new");
        File.WriteAllText(failedNewPath, "collision content");

        store.WriteSession(session);
        var applier = CreateApplierWithNullRestart(store, helperPath);
        var result = await applier.RunAsync(session.SessionId);

        Assert.Equal(SelfUpdateApplier.ExitCodeRestartFailed, result);
        Assert.True(File.Exists(failedNewPath));
        Assert.Equal("collision content", File.ReadAllText(failedNewPath));
    }

    // --- Helpers ---

    private SelfUpdateApplier CreateApplier(UpdateSessionStore store, string currentPath, bool parentRunning = false)
    {
        _startedProcesses.Clear();
        return new SelfUpdateApplier(
            store,
            _logger,
            () => currentPath,
            path => MakeVersionInfoForPath(path, currentPath),
            _ => parentRunning,
            psi => { _startedProcesses.Add(psi); return null; });
    }

    private SelfUpdateApplier CreateApplierWithNullRestart(UpdateSessionStore store, string currentPath)
    {
        _startedProcesses.Clear();
        return new SelfUpdateApplier(
            store,
            _logger,
            () => currentPath,
            path => MakeVersionInfoForPath(path, currentPath),
            _ => false,
            psi => { _startedProcesses.Add(psi); return null; });
    }

    private SelfUpdateApplier CreateApplierWithThrowingRestart(UpdateSessionStore store, string currentPath)
    {
        _startedProcesses.Clear();
        return new SelfUpdateApplier(
            store,
            _logger,
            () => currentPath,
            path => MakeVersionInfoForPath(path, currentPath),
            _ => false,
            psi => { throw new InvalidOperationException("process start denied"); });
    }

    private static FileVersionInfo MakeVersionInfoForPath(string path, string helperPath)
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location);

        string fileVer;
        string prodVer;
        if (File.Exists(path) && FileHashesMatch(path, helperPath))
        {
            fileVer = "0.1.4.0";
            prodVer = "0.1.4";
        }
        else
        {
            fileVer = "0.1.3.0";
            prodVer = "0.1.3";
        }

        typeof(FileVersionInfo).GetField("_fileVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(info, fileVer);
        typeof(FileVersionInfo).GetField("_productVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(info, prodVer);
        return info;
    }

    private static bool FileHashesMatch(string path1, string path2)
    {
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream1 = File.OpenRead(path1);
            using var stream2 = File.OpenRead(path2);
            var hash1 = md5.ComputeHash(stream1);
            var hash2 = md5.ComputeHash(stream2);
            return hash1.AsSpan().SequenceEqual(hash2);
        }
        catch
        {
            return false;
        }
    }

    private UpdateSession MakePreparedSession(UpdateSessionStore store)
    {
        var session = new UpdateSession
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
            StagedExeSha256 = new string('b', 64),
            OriginalExeSha256 = new string('c', 64)
        };
        var stagedDir = store.GetSessionDir(session.SessionId);
        Directory.CreateDirectory(stagedDir);
        return session;
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

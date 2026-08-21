using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateLifecycleServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly NullLogger _logger = new();
    private readonly UpdateSessionStore _store;

    public UpdateLifecycleServiceTests()
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

    // --- §31 Startup recovery tests ---

    [Fact]
    public void RunStartupMaintenance_AppliedSession_ValidNewTarget_DeletesBackupAndSession()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "new version content");
        var targetSha = HashHelper.ComputeFileSha256(targetPath);

        var session = CreateAppliedSession(targetPath, targetSha);
        var backupPath = Workspace(session).BackupPath;
        File.WriteAllText(backupPath, "old content");
        session.OriginalExeSha256 = HashHelper.ComputeFileSha256(backupPath);
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.False(File.Exists(backupPath));
        Assert.False(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_AppliedSession_BackupShaMismatch_KeepsBackup()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "new version content");
        var targetSha = HashHelper.ComputeFileSha256(targetPath);

        var session = CreateAppliedSession(targetPath, targetSha);
        session.OriginalExeSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
        var backupPath = Workspace(session).BackupPath;
        File.WriteAllText(backupPath, "old content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(File.Exists(backupPath));
        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_AppliedSession_TargetMismatch_KeepsEverything()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "new version content");

        var session = CreateAppliedSession(targetPath, "0000000000000000000000000000000000000000000000000000000000000000");
        session.OriginalExeSha256 = new string('c', 64);
        var backupPath = Workspace(session).BackupPath;
        File.WriteAllText(backupPath, "old content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public void RunStartupMaintenance_PreparedSession_OldTarget_CleansUp()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "old content");
        var originalSha = HashHelper.ComputeFileSha256(targetPath);

        var session = CreatePreparedSession(targetPath);
        session.OriginalExeSha256 = originalSha;
        var candidatePath = Workspace(session).CandidatePath;
        File.WriteAllText(candidatePath, "new content");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(candidatePath);
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.False(File.Exists(candidatePath));
        Assert.False(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_PreparedSession_NewTarget_DoesNotCleanUp()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "new content");
        var newSha = HashHelper.ComputeFileSha256(targetPath);

        var session = CreatePreparedSession(targetPath);
        session.OriginalExeSha256 = new string('c', 64);
        session.StagedExeSha256 = newSha;
        var candidatePath = Workspace(session).CandidatePath;
        File.WriteAllText(candidatePath, "new content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_PersistentSessionLock_DoesNotReportSuccess()
    {
        var targetPath = CreateCurrentTarget();
        var session = CreateStagedSession(targetPath, DateTimeOffset.UtcNow - TimeSpan.FromDays(8));
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        File.WriteAllText(Path.Combine(sessionDir, session.PackageAssetName), "locked helper");
        _store.DeleteFileOverride = _ => false;

        CreateService(targetPath).RunStartupMaintenance();

        Assert.True(Directory.Exists(sessionDir));
        Assert.Contains(_logger.Warnings, warning => warning.Contains("retained", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logger.DebugMessages, message => message.Contains("cleaned up session directory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunStartupMaintenance_PreparedSession_CandidateShaMismatch_DoesNotDelete()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "old content");
        var originalSha = HashHelper.ComputeFileSha256(targetPath);

        var session = CreatePreparedSession(targetPath);
        session.OriginalExeSha256 = originalSha;
        session.StagedExeSha256 = new string('b', 64);
        var candidatePath = Workspace(session).CandidatePath;
        File.WriteAllText(candidatePath, "different content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(File.Exists(candidatePath));
        Assert.Equal("different content", File.ReadAllText(candidatePath));
        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_AbandonedPreparedForeignTarget_PreservesCandidateAndSession()
    {
        var currentPath = Path.Combine(_tempRoot, "current", "BDO-UA-Client.exe");
        var foreignPath = Path.Combine(_tempRoot, "foreign", "BDO-UA-Client.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
        File.WriteAllText(currentPath, "old content");
        File.WriteAllText(foreignPath, "old content");

        var session = CreatePreparedSession(foreignPath);
        session.CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(8);
        session.OriginalExeSha256 = HashHelper.ComputeFileSha256(foreignPath);
        var candidatePath = Workspace(session).CandidatePath;
        File.WriteAllText(candidatePath, "new content");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(candidatePath);
        _store.WriteSession(session);

        var service = CreateService(currentPath);
        service.RunStartupMaintenance();

        Assert.True(File.Exists(candidatePath));
        Assert.True(File.Exists(foreignPath));
        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_AbandonedAppliedForeignTarget_PreservesBackupAndSession()
    {
        var currentPath = Path.Combine(_tempRoot, "current", "BDO-UA-Client.exe");
        var foreignPath = Path.Combine(_tempRoot, "foreign", "BDO-UA-Client.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
        File.WriteAllText(currentPath, "old content");
        File.WriteAllText(foreignPath, "new content");

        var session = CreateAppliedSession(foreignPath, HashHelper.ComputeFileSha256(foreignPath));
        session.CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(8);
        var backupPath = Workspace(session).BackupPath;
        File.WriteAllText(backupPath, "old content");
        session.OriginalExeSha256 = HashHelper.ComputeFileSha256(backupPath);
        _store.WriteSession(session);

        var service = CreateService(currentPath);
        service.RunStartupMaintenance();

        Assert.True(File.Exists(backupPath));
        Assert.True(File.Exists(foreignPath));
        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    // --- §32 Retention tests ---

    [Fact]
    public void RunStartupMaintenance_OldStagedSession_DeletesSession()
    {
        var session = new UpdateSession
        {
            SchemaVersion = 1,
            SessionId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(8),
            State = UpdateSession.StateStaged,
            CurrentVersion = "0.1.3",
            TargetVersion = "0.1.4",
            TargetTag = "v0.1.4",
            TargetPath = @"C:\test\BDO-UA-Client.exe",
            ParentPid = 12345,
            PackageAssetName = "BDO-UA-Client.exe",
            PackageSha256 = new string('a', 64),
            StagedExeSha256 = new string('b', 64)
        };
        _store.WriteSession(session);

        var targetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        session.TargetPath = targetPath;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "old content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.False(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_RecentStagedSession_KeepsSession()
    {
        var session = new UpdateSession
        {
            SchemaVersion = 1,
            SessionId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(1),
            State = UpdateSession.StateStaged,
            CurrentVersion = "0.1.3",
            TargetVersion = "0.1.4",
            TargetTag = "v0.1.4",
            TargetPath = @"C:\test\BDO-UA-Client.exe",
            ParentPid = 12345,
            PackageAssetName = "BDO-UA-Client.exe",
            PackageSha256 = new string('a', 64),
            StagedExeSha256 = new string('b', 64)
        };
        _store.WriteSession(session);

        var targetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        session.TargetPath = targetPath;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "old content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_NonGuidDirectory_NotTouched()
    {
        var updatesDir = _appPaths.UpdatesDir;
        var nonGuidDir = Path.Combine(updatesDir, "not-a-guid");
        Directory.CreateDirectory(nonGuidDir);
        File.WriteAllText(Path.Combine(nonGuidDir, "test.txt"), "data");

        var targetPath = Path.Combine(_tempRoot, "target", "BDO-UA-Client.exe");
        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(Directory.Exists(nonGuidDir));
        Assert.True(File.Exists(Path.Combine(nonGuidDir, "test.txt")));
    }

    [Fact]
    public void RunStartupMaintenance_RetentionBoundary_ExactlySevenDays_IsEligible()
    {
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var targetPath = CreateCurrentTarget();
        var session = CreateStagedSession(targetPath, now - TimeSpan.FromDays(7));
        _store.WriteSession(session);

        CreateService(targetPath, now).RunStartupMaintenance();

        Assert.False(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_RetentionBoundary_OneTickRecent_IsRetained()
    {
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var targetPath = CreateCurrentTarget();
        var session = CreateStagedSession(targetPath, now - TimeSpan.FromDays(7) + TimeSpan.FromTicks(1));
        _store.WriteSession(session);

        CreateService(targetPath, now).RunStartupMaintenance();

        Assert.True(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    [Fact]
    public void RunStartupMaintenance_RetentionBoundary_OneTickOld_IsEligible()
    {
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var targetPath = CreateCurrentTarget();
        var session = CreateStagedSession(targetPath, now - TimeSpan.FromDays(7) - TimeSpan.FromTicks(1));
        _store.WriteSession(session);

        CreateService(targetPath, now).RunStartupMaintenance();

        Assert.False(Directory.Exists(_store.GetSessionDir(session.SessionId)));
    }

    // --- §33 Delete safety tests ---

    [Fact]
    public void RunStartupMaintenance_AppliedSession_FailedNewShaMismatch_KeepsFile()
    {
        var targetDir = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "new version content");
        var targetSha = HashHelper.ComputeFileSha256(targetPath);

        var session = CreateAppliedSession(targetPath, targetSha);
        session.OriginalExeSha256 = new string('c', 64);
        var failedNewPath = Workspace(session).FailedNewPath;
        File.WriteAllText(failedNewPath, "unrelated content");
        _store.WriteSession(session);

        var service = CreateService(targetPath);
        service.RunStartupMaintenance();

        Assert.True(File.Exists(failedNewPath));
        Assert.Equal("unrelated content", File.ReadAllText(failedNewPath));
    }

    // --- Helpers ---

    private ReplacementWorkspace Workspace(UpdateSession session)
    {
        var workspace = ReplacementWorkspace.Derive(_appPaths, session.SessionId, session.TargetPath);
        workspace.EnsureDirectory();
        return workspace;
    }

    private UpdateLifecycleService CreateService(string currentProcessPath, DateTimeOffset? utcNow = null)
    {
        return new UpdateLifecycleService(
            _store,
            _appPaths,
            _logger,
            () => currentProcessPath,
            path =>
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location);
                var version = File.Exists(path) && File.ReadAllText(path).Contains("new", StringComparison.Ordinal)
                    ? ("0.1.4.0", "0.1.4")
                    : ("0.1.3.0", "0.1.3");
                typeof(System.Diagnostics.FileVersionInfo)
                    .GetField("_fileVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(info, version.Item1);
                typeof(System.Diagnostics.FileVersionInfo)
                    .GetField("_productVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(info, version.Item2);
                return info;
            },
            utcNow is null ? null : () => utcNow.Value);
    }

    private string CreateCurrentTarget()
    {
        var targetPath = Path.Combine(_tempRoot, "current", "BDO-UA-Client.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "old content");
        return targetPath;
    }

    private static UpdateSession CreateStagedSession(string targetPath, DateTimeOffset createdAt)
    {
        return new UpdateSession
        {
            SchemaVersion = 1,
            SessionId = Guid.NewGuid().ToString("D"),
            CreatedAt = createdAt,
            State = UpdateSession.StateStaged,
            CurrentVersion = "0.1.3",
            TargetVersion = "0.1.4",
            TargetTag = "v0.1.4",
            TargetPath = targetPath,
            ParentPid = 12345,
            PackageAssetName = "BDO-UA-Client.exe",
            PackageSha256 = new string('a', 64),
            StagedExeSha256 = new string('b', 64)
        };
    }

    private UpdateSession CreateAppliedSession(string targetPath, string stagedExeSha)
    {
        return new UpdateSession
        {
            SchemaVersion = 1,
            SessionId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow,
            State = UpdateSession.StateApplied,
            CurrentVersion = "0.1.3",
            TargetVersion = "0.1.4",
            TargetTag = "v0.1.4",
            TargetPath = targetPath,
            ParentPid = 12345,
            PackageAssetName = "BDO-UA-Client.exe",
            PackageSha256 = new string('a', 64),
            StagedExeSha256 = stagedExeSha,
            OriginalExeSha256 = new string('c', 64)
        };
    }

    private UpdateSession CreatePreparedSession(string targetPath)
    {
        return new UpdateSession
        {
            SchemaVersion = 1,
            SessionId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow,
            State = UpdateSession.StatePrepared,
            CurrentVersion = "0.1.3",
            TargetVersion = "0.1.4",
            TargetTag = "v0.1.4",
            TargetPath = targetPath,
            ParentPid = 12345,
            PackageAssetName = "BDO-UA-Client.exe",
            PackageSha256 = new string('a', 64),
            StagedExeSha256 = new string('b', 64),
            OriginalExeSha256 = new string('c', 64)
        };
    }

    private class NullLogger : ILogger
    {
        public List<string> DebugMessages { get; } = new();
        public List<string> Warnings { get; } = new();
        public void Debug(string message) => DebugMessages.Add(message);
        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message) { }
    }
}

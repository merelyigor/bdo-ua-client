using System.IO;
using BdoClient.Logging;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class SelfUpdateReplacementRetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"bdo-replace-test-{Guid.NewGuid():N}");

    public SelfUpdateReplacementRetryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void TransientSharingViolation_RetriesAndSucceeds()
    {
        var attempts = 0;
        var clock = new FakeClock();
        var applier = Create((source, target, backup) =>
        {
            if (++attempts == 1) throw new IOException("sharing", unchecked((int)0x80070020));
            File.Copy(source, target, true);
            File.Copy(target, backup, true);
        }, clock);
        var paths = CreateFiles();

        applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup);

        Assert.Equal(2, attempts);
        Assert.Equal("new", File.ReadAllText(paths.target));
    }

    [Fact]
    public void RepeatedTransientSharingViolations_FailBounded()
    {
        var attempts = 0;
        var clock = new FakeClock();
        var logger = new RecordingLogger();
        var applier = Create((_, _, _) =>
        {
            attempts++;
            throw new IOException("sharing", unchecked((int)0x80070020));
        }, clock, logger);
        var paths = CreateFiles();

        Assert.Throws<IOException>(() => applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup));

        Assert.InRange(clock.Milliseconds, 30000, 30500);
        Assert.True(attempts > 1);
        Assert.Equal("old", File.ReadAllText(paths.target));
        Assert.False(File.Exists(paths.backup));
        Assert.Single(logger.ErrorLines, line => line.Contains("transient lock exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public void TransientSharingViolation_PastFiveSecondsThenSucceeds()
    {
        var attempts = 0;
        var clock = new FakeClock();
        var applier = Create((source, target, backup) =>
        {
            if (clock.Milliseconds < 6000)
            {
                attempts++;
                throw new IOException("sharing", unchecked((int)0x80070020));
            }

            File.Copy(source, target, true);
            File.Copy(target, backup, true);
        }, clock);
        var paths = CreateFiles();

        applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup);

        Assert.True(clock.Milliseconds >= 6000);
        Assert.True(attempts > 1);
        Assert.Equal("new", File.ReadAllText(paths.target));
    }

    [Fact]
    public void LockViolation_RetriesAndSucceeds()
    {
        var attempts = 0;
        var clock = new FakeClock();
        var applier = Create((source, target, backup) =>
        {
            if (++attempts == 1) throw new IOException("lock", unchecked((int)0x80070021));
            File.Copy(source, target, true);
            File.Copy(target, backup, true);
        }, clock);
        var paths = CreateFiles();

        applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup);

        Assert.Equal(2, attempts);
        Assert.Equal("new", File.ReadAllText(paths.target));
    }

    [Fact]
    public void NonTransientFailure_IsNotRetried()
    {
        var attempts = 0;
        var applier = Create((_, _, _) =>
        {
            attempts++;
            throw new IOException("access denied", unchecked((int)0x80070005));
        });
        var paths = CreateFiles();

        Assert.Throws<IOException>(() => applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup));
        Assert.Equal(1, attempts);
        Assert.Equal("old", File.ReadAllText(paths.target));
    }

    [Fact]
    public void TransientLockLogging_IsThrottledAndReportsSuccess()
    {
        var clock = new FakeClock();
        var logger = new RecordingLogger();
        var applier = Create((source, target, backup) =>
        {
            if (clock.Milliseconds < 2200)
                throw new IOException("sharing", unchecked((int)0x80070020));
            File.Copy(source, target, true);
            File.Copy(target, backup, true);
        }, clock, logger);
        var paths = CreateFiles();

        applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup);

        Assert.InRange(logger.WarningLines.Count, 2, 4);
        Assert.Contains(logger.WarningLines, line => line.Contains("temporarily locked", StringComparison.Ordinal));
        Assert.Contains(logger.WarningLines, line => line.Contains("still locked", StringComparison.Ordinal));
        Assert.Contains(logger.InfoLines, line => line.Contains("succeeded after transient lock wait", StringComparison.Ordinal));
    }

    private SelfUpdateApplier Create(Action<string, string, string> replace, FakeClock? clock = null, RecordingLogger? logger = null)
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        var effectiveClock = clock ?? new FakeClock();
        var effectiveLogger = logger ?? new RecordingLogger();
        return new SelfUpdateApplier(new UpdateSessionStore(paths, effectiveLogger), effectiveLogger,
            () => "", _ => System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location),
            _ => false, _ => null, replace, () => effectiveClock.Milliseconds, effectiveClock.Sleep);
    }

    private (string candidate, string target, string backup) CreateFiles()
    {
        var candidate = Path.Combine(_root, "candidate.new");
        var target = Path.Combine(_root, "target.exe");
        var backup = Path.Combine(_root, "backup.bak");
        File.WriteAllText(candidate, "new");
        File.WriteAllText(target, "old");
        return (candidate, target, backup);
    }

    private sealed class FakeClock
    {
        public long Milliseconds { get; private set; }

        public void Sleep(int milliseconds) => Milliseconds += milliseconds;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> InfoLines { get; } = [];
        public List<string> WarningLines { get; } = [];
        public List<string> ErrorLines { get; } = [];

        public void Debug(string message) { }
        public void Info(string message) => InfoLines.Add(message);
        public void Warning(string message) => WarningLines.Add(message);
        public void Error(string message) => ErrorLines.Add(message);
    }
}

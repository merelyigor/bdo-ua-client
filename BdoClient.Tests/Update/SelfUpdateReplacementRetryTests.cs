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
        var applier = Create((source, target, backup) =>
        {
            if (++attempts == 1) throw new IOException("sharing", unchecked((int)0x80070020));
            File.Copy(source, target, true);
            File.Copy(target, backup, true);
        });
        var paths = CreateFiles();

        applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup);

        Assert.Equal(2, attempts);
        Assert.Equal("new", File.ReadAllText(paths.target));
    }

    [Fact]
    public void RepeatedTransientSharingViolations_FailBounded()
    {
        var attempts = 0;
        var applier = Create((_, _, _) =>
        {
            attempts++;
            throw new IOException("sharing", unchecked((int)0x80070020));
        });
        var paths = CreateFiles();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<IOException>(() => applier.ReplaceWithRetryForTests(paths.candidate, paths.target, paths.backup));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(6));
        Assert.True(attempts > 1);
        Assert.Equal("old", File.ReadAllText(paths.target));
        Assert.False(File.Exists(paths.backup));
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

    private SelfUpdateApplier Create(Action<string, string, string> replace)
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        return new SelfUpdateApplier(new UpdateSessionStore(paths, new NullLogger()), new NullLogger(),
            () => "", _ => System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location),
            _ => false, _ => null, replace);
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

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

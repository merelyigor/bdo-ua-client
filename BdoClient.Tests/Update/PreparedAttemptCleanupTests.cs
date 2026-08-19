using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class PreparedAttemptCleanupTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly NullLogger _logger = new();

    public PreparedAttemptCleanupTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void TryDeleteCandidate_MatchingSha_DeletesCandidate()
    {
        var session = CreateSession("verified candidate");
        var candidatePath = GetCandidatePath(session);
        File.WriteAllText(candidatePath, "verified candidate");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(candidatePath);

        var result = PreparedAttemptCleanup.TryDeleteCandidate(session, _logger);

        Assert.True(result);
        Assert.False(File.Exists(candidatePath));
    }

    [Fact]
    public void TryDeleteCandidate_MismatchedSha_PreservesCandidate()
    {
        var session = CreateSession("expected content");
        var candidatePath = GetCandidatePath(session);
        File.WriteAllText(candidatePath, "unrelated content");
        session.StagedExeSha256 = HashHelper.ComputeSha256(
            System.Text.Encoding.UTF8.GetBytes("expected content"));

        var result = PreparedAttemptCleanup.TryDeleteCandidate(session, _logger);

        Assert.False(result);
        Assert.True(File.Exists(candidatePath));
    }

    [Fact]
    public void TryDeleteCandidate_NonFilePath_PreservesPath()
    {
        var session = CreateSession("expected content");
        var candidatePath = GetCandidatePath(session);
        Directory.CreateDirectory(candidatePath);

        var result = PreparedAttemptCleanup.TryDeleteCandidate(session, _logger);

        Assert.False(result);
        Assert.True(Directory.Exists(candidatePath));
    }

    private UpdateSession CreateSession(string content)
    {
        var targetPath = Path.Combine(_tempRoot, "BDO-UA-Client.exe");
        File.WriteAllText(targetPath, "old content");
        return new UpdateSession
        {
            SessionId = Guid.NewGuid().ToString("D"),
            TargetPath = targetPath,
            StagedExeSha256 = HashHelper.ComputeSha256(System.Text.Encoding.UTF8.GetBytes(content))
        };
    }

    private static string GetCandidatePath(UpdateSession session)
    {
        return Path.Combine(
            Path.GetDirectoryName(session.TargetPath)!,
            $"{Path.GetFileName(session.TargetPath)}.update-{session.SessionId}.new");
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

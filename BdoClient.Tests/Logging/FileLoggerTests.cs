using System.Text.RegularExpressions;
using BdoClient.Logging;

namespace BdoClient.Tests.Logging;

public class FileLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private FileLogger CreateLogger() => new(_tempDir);

    private string GetLogFilePath()
    {
        return Path.Combine(_tempDir, $"bdo-ua-client_{DateTime.Now:yyyy-MM-dd}.log");
    }

    [Fact]
    public void Debug_WritesDebugLevel()
    {
        var logger = CreateLogger();
        logger.Debug("test");
        var line = File.ReadAllLines(GetLogFilePath())[0];
        Assert.Contains("[DEBUG]", line);
    }

    [Fact]
    public void Info_WritesInfoLevel()
    {
        var logger = CreateLogger();
        logger.Info("test");
        var line = File.ReadAllLines(GetLogFilePath())[0];
        Assert.Contains("[INFO]", line);
    }

    [Fact]
    public void Warning_WritesWarnLevel()
    {
        var logger = CreateLogger();
        logger.Warning("test");
        var line = File.ReadAllLines(GetLogFilePath())[0];
        Assert.Contains("[WARN]", line);
    }

    [Fact]
    public void Error_WritesErrorLevel()
    {
        var logger = CreateLogger();
        logger.Error("test");
        var line = File.ReadAllLines(GetLogFilePath())[0];
        Assert.Contains("[ERROR]", line);
    }

    [Fact]
    public void Format_ContainsTimestamp_Level_Message()
    {
        var logger = CreateLogger();
        logger.Info("hello");
        var line = File.ReadAllLines(GetLogFilePath())[0];
        Assert.True(
            Regex.IsMatch(line, @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO\] hello"),
            $"Line did not match expected format: {line}");
    }

    [Fact]
    public void FileName_MatchesCurrentLocalDate()
    {
        var logger = CreateLogger();
        logger.Info("test");
        var expected = Path.Combine(_tempDir, $"bdo-ua-client_{DateTime.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(expected), $"Expected log file not found: {expected}");
    }

    [Fact]
    public void MultipleWrites_AppendNotOverwrite()
    {
        var logger = CreateLogger();
        logger.Info("line1");
        logger.Info("line2");
        var lines = File.ReadAllLines(GetLogFilePath());
        Assert.Equal(2, lines.Length);
        Assert.Contains("line1", lines[0]);
        Assert.Contains("line2", lines[1]);
    }

    [Fact]
    public void MultilineMessage_NormalizedToOneRecord()
    {
        var logger = CreateLogger();
        logger.Info("foo\r\nbar");
        var lines = File.ReadAllLines(GetLogFilePath());
        Assert.Single(lines);
        Assert.Contains("foo bar", lines[0]);
    }

    [Fact]
    public async Task ConcurrentWrites_AllPersisted()
    {
        var logger = CreateLogger();
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                    logger.Info($"msg {idx}-{j}");
            });
        }
        await Task.WhenAll(tasks);
        var lines = File.ReadAllLines(GetLogFilePath());
        Assert.Equal(100, lines.Length);
    }

    [Fact]
    public void InvalidDirectory_DoesNotThrow()
    {
        var logger = new FileLogger(@"Z:\nonexistent\path");
        var exception = Record.Exception(() =>
        {
            logger.Info("test");
            logger.Warning("test");
            logger.Error("test");
        });
        Assert.Null(exception);
    }

    [Fact]
    public void Retention_TodayLog_Preserved()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_2026-08-18.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.True(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_2026-08-18.log")));
    }

    [Fact]
    public void Retention_1DayOldLog_Preserved()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_2026-08-17.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.True(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_2026-08-17.log")));
    }

    [Fact]
    public void Retention_14DayOldLog_Preserved()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_2026-08-04.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.True(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_2026-08-04.log")));
    }

    [Fact]
    public void Retention_15DayOldLog_Deleted()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_2026-08-03.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.False(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_2026-08-03.log")));
    }

    [Fact]
    public void Retention_16DayOldLog_Deleted()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_2026-08-02.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.False(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_2026-08-02.log")));
    }

    [Fact]
    public void Retention_30DayOldLog_Deleted()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_2026-07-19.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.False(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_2026-07-19.log")));
    }

    [Fact]
    public void Retention_UnrelatedFile_Preserved()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "unrelated.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.True(File.Exists(Path.Combine(_tempDir, "unrelated.log")));
    }

    [Fact]
    public void Retention_MalformedFilename_Preserved()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0);
        var logger = new FileLogger(_tempDir, () => now);
        File.WriteAllText(Path.Combine(_tempDir, "bdo-ua-client_bad-date.log"), "data");
        logger.CleanupOldLogs(now);
        Assert.True(File.Exists(Path.Combine(_tempDir, "bdo-ua-client_bad-date.log")));
    }

    [Fact]
    public void Retention_NonexistentDirectory_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
        {
            var logger = new FileLogger(@"Z:\nonexistent", () => new DateTime(2026, 8, 18));
        });
        Assert.Null(exception);
    }
}

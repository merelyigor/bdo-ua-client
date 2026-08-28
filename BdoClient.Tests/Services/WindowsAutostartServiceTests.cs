using BdoClient.Logging;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class WindowsAutostartServiceTests
{
    private const string ExePath = @"C:\Program Files\BDO UA\BdoClient.exe";
    private const string ValueName = "BDO-UA-Client";

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private static WindowsAutostartService CreateService(string path = ExePath)
        => new(path, new NullLogger());

    [Fact]
    public void BuildRunCommand_QuotesPathWithSpaces()
    {
        var service = CreateService();
        Assert.Equal("\"C:\\Program Files\\BDO UA\\BdoClient.exe\" --background", service.BuildRunCommand());
    }

    [Fact]
    public void BuildRunCommand_QuotesSimplePath()
    {
        var service = CreateService(@"C:\BdoClient.exe");
        Assert.Equal("\"C:\\BdoClient.exe\" --background", service.BuildRunCommand());
    }

    [Fact]
    public void BuildRunCommand_ContainsExactlyOneBackgroundArgument()
    {
        var command = CreateService().BuildRunCommand();
        Assert.Equal(1, command.Split(" --background").Length - 1);
        Assert.EndsWith("--background", command);
    }

    [Fact]
    public void MatchesCanonicalCommand_RecognizesCurrentCommand()
    {
        var command = CreateService().BuildRunCommand();
        Assert.True(WindowsAutostartService.MatchesCanonicalCommand(command, ExePath));
    }

    [Fact]
    public void MatchesCanonicalCommand_IsCaseInsensitiveForWindowsPaths()
    {
        var lower = "\"c:\\program files\\bdo ua\\bdoclient.exe\" --background";
        Assert.True(WindowsAutostartService.MatchesCanonicalCommand(lower, ExePath));
    }

    [Fact]
    public void MatchesCanonicalCommand_RejectsStaleExecutablePath()
    {
        var stale = "\"C:\\Other\\BdoClient.exe\" --background";
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand(stale, ExePath));
    }

    [Fact]
    public void MatchesCanonicalCommand_RejectsMissingBackgroundArgument()
    {
        var noBackground = "\"C:\\Program Files\\BDO UA\\BdoClient.exe\"";
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand(noBackground, ExePath));
    }

    [Fact]
    public void MatchesCanonicalCommand_RejectsNullAndEmpty()
    {
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand(null, ExePath));
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand("", ExePath));
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand("   ", ExePath));
    }

    [Fact]
    public void MatchesCanonicalCommand_RejectsMalformedValue()
    {
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand("garbage --background", ExePath));
        Assert.False(WindowsAutostartService.MatchesCanonicalCommand("\"C:\\x\" --background --extra", ExePath));
    }

    [Fact]
    public void Constructor_RejectsNonFullyQualifiedPath()
    {
        Assert.Throws<ArgumentException>(() => CreateService("BdoClient.exe"));
        Assert.Throws<ArgumentException>(() => CreateService("relative\\BdoClient.exe"));
    }

    [Fact]
    public void Constructor_RejectsEmbeddedQuote()
    {
        Assert.Throws<ArgumentException>(() => CreateService("\"C:\\BdoClient.exe\""));
    }

    [Fact]
    public void Constructor_RejectsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => CreateService(""));
        Assert.Throws<ArgumentException>(() => CreateService("   "));
        Assert.Throws<ArgumentException>(() => CreateService(null!));
    }
}

using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class ApplicationCommandLineTests
{
    [Fact]
    public void Parse_NoArgs_IsNotApplyUpdateMode()
    {
        var cmd = ApplicationCommandLine.Parse(Array.Empty<string>());
        Assert.False(cmd.IsApplyUpdateMode);
        Assert.Null(cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_PrototypeFlag_IsNotApplyUpdateMode()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--prototype" });
        Assert.False(cmd.IsApplyUpdateMode);
    }

    [Fact]
    public void Parse_ApplyUpdateWithValidGuid_IsApplyUpdateMode()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", sessionId });
        Assert.True(cmd.IsApplyUpdateMode);
        Assert.Equal(sessionId, cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_ApplyUpdateWithInvalidGuid_IsNotApplyUpdateMode()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", "not-a-guid" });
        Assert.False(cmd.IsApplyUpdateMode);
        Assert.Null(cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_ApplyUpdateWithBraceFormat_IsNotApplyUpdateMode()
    {
        var guid = Guid.NewGuid();
        var brace = guid.ToString("B");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", brace });
        Assert.False(cmd.IsApplyUpdateMode);
        Assert.Null(cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_ApplyUpdateWithNFormat_IsNotApplyUpdateMode()
    {
        var guid = Guid.NewGuid();
        var n = guid.ToString("N");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", n });
        Assert.False(cmd.IsApplyUpdateMode);
        Assert.Null(cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_ApplyUpdateMissingArg_IsNotApplyUpdateMode()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update" });
        Assert.False(cmd.IsApplyUpdateMode);
        Assert.Null(cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_ApplyUpdateAmongOtherArgs_IsApplyUpdateMode()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--some-flag", "--apply-update", sessionId, "--other" });
        Assert.True(cmd.IsApplyUpdateMode);
        Assert.Equal(sessionId, cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_MultipleApplyUpdate_UsesFirst()
    {
        var session1 = Guid.NewGuid().ToString("D");
        var session2 = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", session1, "--apply-update", session2 });
        Assert.True(cmd.IsApplyUpdateMode);
        Assert.Equal(session1, cmd.ApplyUpdateSessionId);
    }
}

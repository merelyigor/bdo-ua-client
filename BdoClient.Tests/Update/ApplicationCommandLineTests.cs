using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class ApplicationCommandLineTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsNormal()
    {
        var cmd = ApplicationCommandLine.Parse(Array.Empty<string>());
        Assert.Equal(CommandLineMode.Normal, cmd.Mode);
        Assert.Null(cmd.ApplyUpdateSessionId);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_UnknownArgs_ReturnsNormal()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--unknown", "value" });
        Assert.Equal(CommandLineMode.Normal, cmd.Mode);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_ExactBackground_ReturnsNormalWithBackground()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--background" });
        Assert.Equal(CommandLineMode.Normal, cmd.Mode);
        Assert.True(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_BackgroundWithExtraArg_ReturnsNormalWithoutBackground()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--background", "extra" });
        Assert.Equal(CommandLineMode.Normal, cmd.Mode);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_DuplicateBackground_ReturnsNormalWithoutBackground()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--background", "--background" });
        Assert.Equal(CommandLineMode.Normal, cmd.Mode);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_SingleUnknownArg_ReturnsNormalWithoutBackground()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--unknown" });
        Assert.Equal(CommandLineMode.Normal, cmd.Mode);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_BackgroundBeforeApplyUpdate_ReturnsInvalidWithoutBackground()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--background", "--apply-update", sessionId });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_BackgroundAfterApplyUpdate_ReturnsInvalidWithoutBackground()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", sessionId, "--background" });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
        Assert.False(cmd.StartInBackground);
    }

    [Fact]
    public void Parse_ValidApplyCommand_ReturnsApplyUpdate()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", sessionId });
        Assert.Equal(CommandLineMode.ApplyUpdate, cmd.Mode);
        Assert.Equal(sessionId, cmd.ApplyUpdateSessionId);
    }

    [Fact]
    public void Parse_MissingId_ReturnsInvalid()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update" });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_InvalidId_ReturnsInvalid()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", "not-a-guid" });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_BraceFormat_ReturnsInvalid()
    {
        var guid = Guid.NewGuid();
        var brace = guid.ToString("B");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", brace });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_NFormat_ReturnsInvalid()
    {
        var guid = Guid.NewGuid();
        var n = guid.ToString("N");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", n });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_ExtraTrailingArg_ReturnsInvalid()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", sessionId, "extra" });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_PrefixUnrelatedArg_ReturnsInvalid()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "x", "--apply-update", sessionId });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_MultipleApplyUpdate_ReturnsInvalid()
    {
        var s1 = Guid.NewGuid().ToString("D");
        var s2 = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", s1, "--apply-update", s2 });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_WhitespaceId_ReturnsInvalid()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", "  " });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_EmptyId_ReturnsInvalid()
    {
        var cmd = ApplicationCommandLine.Parse(new[] { "--apply-update", "" });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_FlagAtIndex1_ReturnsInvalid()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "x", "--apply-update", sessionId });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }

    [Fact]
    public void Parse_FlagNotAtStart_ReturnsInvalid()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var cmd = ApplicationCommandLine.Parse(new[] { "--other", "--apply-update", sessionId });
        Assert.Equal(CommandLineMode.InvalidApplyUpdate, cmd.Mode);
    }
}

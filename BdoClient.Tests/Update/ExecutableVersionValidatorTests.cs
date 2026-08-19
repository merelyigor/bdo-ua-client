using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class ExecutableVersionValidatorTests
{
    [Fact]
    public void Valid_Passes()
    {
        Assert.True(ExecutableVersionValidator.Validate("1.2.3.0", "1.2.3", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void WrongFileBuild_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate("1.2.3.1", "1.2.3", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void WrongTargetVersion_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate("1.2.4.0", "1.2.4", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void FileVersionMetadata_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate("1.2.3+sha", "1.2.3", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void ProductVersionDev_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate("1.2.3.0", "1.2.3-dev", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void NullFileVersion_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate(null, "1.2.3", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void EmptyProductVersion_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate("1.2.3.0", "", new AppVersion(1, 2, 3), out _));
    }

    [Fact]
    public void WhitespaceVersion_Fails()
    {
        Assert.False(ExecutableVersionValidator.Validate("  ", "1.2.3", new AppVersion(1, 2, 3), out _));
    }
}

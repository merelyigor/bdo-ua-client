using BdoClient.Logging;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateManifestValidatorTests
{
    private static readonly ILogger Logger = new NullLogger();

    private static UpdateManifest ValidManifest() => new()
    {
        SchemaVersion = 1,
        Version = "0.1.4",
        Tag = "v0.1.4",
        CommitSha = "74875dfcc6762ec0edb75c40e225150f94fa45e5",
        AssetName = "BDO-UA-Client.exe",
        Sha256 = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
        Platform = "win-x64",
        WorkflowRunId = "32211040254"
    };

    private static UpdateCandidate MakeCandidate(string tag, AppVersion version)
    {
        return new UpdateCandidate(version, tag, new GitHubRelease { TagName = tag });
    }

    [Fact]
    public void ValidManifest_Passes()
    {
        var validator = new UpdateManifestValidator(Logger);
        var result = validator.Validate(ValidManifest(), MakeCandidate("v0.1.4", new AppVersion(0, 1, 4)));
        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedSha256);
    }

    [Fact]
    public void WrongSchema_Fails()
    {
        var manifest = ValidManifest();
        manifest.SchemaVersion = 2;
        var result = new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4)));
        Assert.False(result.IsValid);
        Assert.Contains("schema_version", result.ErrorMessage);
    }

    [Fact]
    public void MissingVersion_Fails()
    {
        var manifest = ValidManifest();
        manifest.Version = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void VersionMismatch_Fails()
    {
        var manifest = ValidManifest();
        manifest.Version = "0.1.5";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void TagMismatch_Fails()
    {
        var manifest = ValidManifest();
        manifest.Tag = "v0.1.5";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void UpperCaseVTag_Fails()
    {
        var manifest = ValidManifest();
        manifest.Tag = "V0.1.4";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void WrongPlatform_Fails()
    {
        var manifest = ValidManifest();
        manifest.Platform = "linux-x64";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MissingAssetName_Fails()
    {
        var manifest = ValidManifest();
        manifest.AssetName = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void NonCanonicalAssetName_Fails()
    {
        var manifest = ValidManifest();
        manifest.AssetName = "BDO-UA-Client-v0.1.4-win-x64.zip";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void InvalidShaLength_Fails()
    {
        var manifest = ValidManifest();
        manifest.Sha256 = "abc123";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void InvalidShaChars_Fails()
    {
        var manifest = ValidManifest();
        manifest.Sha256 = new string('z', 64);
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MissingCommitSha_Fails()
    {
        var manifest = ValidManifest();
        manifest.CommitSha = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MalformedCommitSha_Fails()
    {
        var manifest = ValidManifest();
        manifest.CommitSha = "not-hex";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void ShaNormalizedToLower()
    {
        var manifest = ValidManifest();
        manifest.Sha256 = manifest.Sha256!.ToUpperInvariant();
        var result = new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4)));
        Assert.True(result.IsValid);
        Assert.Equal(manifest.Sha256.ToLowerInvariant(), result.NormalizedSha256);
    }

    [Fact]
    public void StringWorkflowRunId_Passes()
    {
        var manifest = ValidManifest();
        Assert.True(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MissingWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void EmptyWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void NonNumericWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "abc123";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void ZeroWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "0";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void NegativeWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "-1";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

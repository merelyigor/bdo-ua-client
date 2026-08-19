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
        AssetName = "BDO-UA-Client-v0.1.4-win-x64.zip",
        Sha256 = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
        Platform = "win-x64",
        WorkflowRunId = 12345
    };

    private static UpdateCandidate MakeCandidate(string tag, AppVersion version)
    {
        return new UpdateCandidate(version, tag, new GitHubRelease { TagName = tag });
    }

    [Fact]
    public void ValidManifest_Passes()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedSha256);
    }

    [Fact]
    public void WrongSchema_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.SchemaVersion = 2;
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
        Assert.Contains("schema_version", result.ErrorMessage);
    }

    [Fact]
    public void MissingVersion_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Version = null;
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void VersionMismatch_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Version = "0.1.5";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
        Assert.Contains("mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TagMismatch_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Tag = "v0.1.5";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
        Assert.Contains("Tag", result.ErrorMessage);
    }

    [Fact]
    public void UpperCaseVTag_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Tag = "V0.1.4";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void WrongPlatform_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Platform = "linux-x64";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
        Assert.Contains("platform", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingAssetName_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.AssetName = null;
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonCanonicalAssetName_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.AssetName = "BDO-UA-Client.zip";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void InvalidShaLength_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Sha256 = "abc123";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
        Assert.Contains("SHA-256", result.ErrorMessage);
    }

    [Fact]
    public void InvalidShaChars_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Sha256 = new string('z', 64);
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MissingCommitSha_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.CommitSha = null;
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MalformedCommitSha_Fails()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.CommitSha = "not-hex";
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ExtraJsonFields_Tolerated()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShaNormalizedToLower()
    {
        var validator = new UpdateManifestValidator(Logger);
        var manifest = ValidManifest();
        manifest.Sha256 = manifest.Sha256!.ToUpperInvariant();
        var candidate = MakeCandidate("v0.1.4", new AppVersion(0, 1, 4));
        var result = validator.Validate(manifest, candidate);
        Assert.True(result.IsValid);
        Assert.Equal(manifest.Sha256.ToLowerInvariant(), result.NormalizedSha256);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

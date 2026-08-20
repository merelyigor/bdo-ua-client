using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class UpdateZipValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"bdo-zip-test-{Guid.NewGuid():N}");

    public UpdateZipValidationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task ExactSingleExe_Succeeds()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var zip = CreateZip(("BDO-UA-Client.exe", payload));
        var result = await UpdatePackageService.ExtractValidatedExeAsync(
            zip, Path.Combine(_root, "staged.exe"), Sha(payload), new AppVersion(0, 1, 8),
            versionValidator: (_, _) => true);
        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public async Task ExtraEntry_FailsClosed()
    {
        var zip = CreateZip(("BDO-UA-Client.exe", new byte[] { 1 }), ("extra.txt", new byte[] { 2 }));
        var result = await UpdatePackageService.ExtractValidatedExeAsync(zip, Path.Combine(_root, "staged.exe"), new string('a', 64), new AppVersion(0, 1, 8));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task NestedExe_FailsClosed()
    {
        var zip = CreateZip(("nested/BDO-UA-Client.exe", new byte[] { 1 }));
        var result = await UpdatePackageService.ExtractValidatedExeAsync(zip, Path.Combine(_root, "staged.exe"), new string('a', 64), new AppVersion(0, 1, 8));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ShaMismatch_FailsClosed()
    {
        var zip = CreateZip(("BDO-UA-Client.exe", new byte[] { 1, 2 }));
        var result = await UpdatePackageService.ExtractValidatedExeAsync(zip, Path.Combine(_root, "staged.exe"), new string('a', 64), new AppVersion(0, 1, 8), versionValidator: (_, _) => true);
        Assert.False(result.IsValid);
        Assert.True(File.Exists(Path.Combine(_root, "staged.exe")));
    }

    [Fact]
    public async Task EmptyZip_FailsClosed()
    {
        var zip = CreateZip();
        var result = await UpdatePackageService.ExtractValidatedExeAsync(zip, Path.Combine(_root, "staged.exe"), new string('a', 64), new AppVersion(0, 1, 8));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MalformedZip_FailsClosed()
    {
        var path = Path.Combine(_root, "bad.zip");
        await File.WriteAllTextAsync(path, "not a zip");
        var result = await UpdatePackageService.ExtractValidatedExeAsync(path, Path.Combine(_root, "staged.exe"), new string('a', 64), new AppVersion(0, 1, 8));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ExactSchema2Bundle_Succeeds()
    {
        var exe = new byte[] { 1, 2, 3, 4 };
        var candidate = Candidate();
        var zip = CreateBundle(candidate, exe, BundleManifest(candidate, Sha(exe)), $"{Sha(exe)}  BDO-UA-Client.exe\n");

        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => true);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Sha(exe), result.ExeSha256);
    }

    [Fact]
    public async Task BundleMissingManifest_FailsClosed()
    {
        var candidate = Candidate();
        var zip = CreateBundle(candidate, new byte[] { 1 }, manifest: null, sums: "abc");
        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => true);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task BundleWrongSchemaVersion_FailsClosed()
    {
        var candidate = Candidate();
        var manifest = BundleManifest(candidate, new string('a', 64)).Replace("schema_version\":2", "schema_version\":1", StringComparison.Ordinal);
        var zip = CreateBundle(candidate, new byte[] { 1 }, manifest, "a  BDO-UA-Client.exe\n");
        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => true);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task BundleWrongVersionOrTag_FailsClosed()
    {
        var candidate = Candidate();
        var manifest = BundleManifest(candidate, new string('a', 64)).Replace("v0.1.4", "v0.1.5", StringComparison.Ordinal);
        var zip = CreateBundle(candidate, new byte[] { 1 }, manifest, "a  BDO-UA-Client.exe\n");
        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => true);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task BundleMissingOrExtraOrNestedEntry_FailsClosed()
    {
        var candidate = Candidate();
        var exe = new byte[] { 1 };
        var manifest = BundleManifest(candidate, Sha(exe));
        var sums = $"{Sha(exe)}  BDO-UA-Client.exe\n";

        foreach (var entries in new[]
        {
            new[] { ("BDO-UA-Client.exe", exe), ("release-manifest.json", Encoding.UTF8.GetBytes(manifest)), ("SHA256SUMS.txt", Encoding.UTF8.GetBytes(sums)) },
            new[] { ("BDO-UA-Client.exe", exe), ("release-manifest.json", Encoding.UTF8.GetBytes(manifest)), ("SHA256SUMS.txt", Encoding.UTF8.GetBytes(sums)), ($"RELEASE_NOTES-v{candidate.Version}.md", Array.Empty<byte>()), ("extra.txt", new byte[] { 9 }) },
            new[] { ("BDO-UA-Client.exe", exe), ("release-manifest.json", Encoding.UTF8.GetBytes(manifest)), ("SHA256SUMS.txt", Encoding.UTF8.GetBytes(sums)), ($"RELEASE_NOTES-v{candidate.Version}.md", Array.Empty<byte>()), ("nested/extra.txt", new byte[] { 9 }) }
        })
        {
            var zip = CreateZip(entries);
            var result = await UpdatePackageService.ExtractValidatedBundleAsync(
                zip, Path.Combine(_root, Guid.NewGuid() + ".exe"), candidate, versionValidator: (_, _) => true);
            Assert.False(result.IsValid);
        }
    }

    [Fact]
    public async Task BundleDuplicateEntry_FailsClosed()
    {
        var candidate = Candidate();
        var exe = new byte[] { 1 };
        var manifest = Encoding.UTF8.GetBytes(BundleManifest(candidate, Sha(exe)));
        var sums = Encoding.UTF8.GetBytes($"{Sha(exe)}  BDO-UA-Client.exe\n");
        var zip = CreateZip(
            ("BDO-UA-Client.exe", exe), ("BDO-UA-Client.exe", exe),
            ("release-manifest.json", manifest), ("SHA256SUMS.txt", sums),
            ($"RELEASE_NOTES-v{candidate.Version}.md", Array.Empty<byte>()));
        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => true);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task BundleMalformedOrMismatchedSums_FailsClosed()
    {
        var candidate = Candidate();
        var exe = new byte[] { 1 };
        var manifest = BundleManifest(candidate, Sha(exe));
        foreach (var sums in new[] { "not sums\n", $"{new string('a', 64)}  BDO-UA-Client.exe\n", $"{Sha(exe)}  BDO-UA-Client.exe\nextra\n" })
        {
            var zip = CreateBundle(candidate, exe, manifest, sums);
            var result = await UpdatePackageService.ExtractValidatedBundleAsync(
                zip, Path.Combine(_root, Guid.NewGuid() + ".exe"), candidate, versionValidator: (_, _) => true);
            Assert.False(result.IsValid);
        }
    }

    [Fact]
    public async Task BundleExtractedExeShaMismatch_FailsClosed()
    {
        var candidate = Candidate();
        var exe = new byte[] { 1 };
        var manifest = BundleManifest(candidate, new string('a', 64));
        var zip = CreateBundle(candidate, exe, manifest, $"{new string('a', 64)}  BDO-UA-Client.exe\n");
        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => true);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task BundleExecutableVersionMismatch_FailsClosed()
    {
        var candidate = Candidate();
        var exe = new byte[] { 1 };
        var manifest = BundleManifest(candidate, Sha(exe));
        var zip = CreateBundle(candidate, exe, manifest, $"{Sha(exe)}  BDO-UA-Client.exe\n");
        var result = await UpdatePackageService.ExtractValidatedBundleAsync(
            zip, Path.Combine(_root, "staged.exe"), candidate, versionValidator: (_, _) => false);
        Assert.False(result.IsValid);
    }

    private string CreateZip(params (string Name, byte[] Data)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid() + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(data);
        }
        return path;
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static UpdateCandidate Candidate() => new(
        new AppVersion(0, 1, 4), "v0.1.4", new GitHubRelease
        {
            TagName = "v0.1.4",
            PublishedAt = DateTimeOffset.UtcNow
        });

    private static string BundleManifest(UpdateCandidate candidate, string sha) =>
        $"{{\"schema_version\":2,\"version\":\"{candidate.Version}\",\"tag\":\"{candidate.TagName}\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client.exe\",\"sha256\":\"{sha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";

    private string CreateBundle(UpdateCandidate candidate, byte[] exe, string? manifest, string sums)
    {
        var entries = new List<(string Name, byte[] Data)>
        {
            ("BDO-UA-Client.exe", exe),
            ("SHA256SUMS.txt", Encoding.UTF8.GetBytes(sums)),
            ($"RELEASE_NOTES-v{candidate.Version}.md", Array.Empty<byte>())
        };
        if (manifest != null)
            entries.Add(("release-manifest.json", Encoding.UTF8.GetBytes(manifest)));
        return CreateZip(entries.ToArray());
    }
}

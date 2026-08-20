using System.IO.Compression;
using System.Security.Cryptography;
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
}

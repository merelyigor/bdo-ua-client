using System.IO.Compression;
using System.Net;
using System.Text;
using BdoClient.Logging;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdatePackageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly NullLogger _logger = new();

    public UpdatePackageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        _appPaths = new AppPaths(_tempRoot);
        _appPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task StageUpdate_MissingManifestAsset_FailsAssetMissing()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", assets: Array.Empty<GitHubReleaseAsset>());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_ManifestDownloadFails_FailsManifestDownload()
    {
        var handler = new QueuedHandler(HttpStatusCode.ServiceUnavailable);
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", assets: ManifestAsset());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.ManifestDownloadFailed, result.Error);
    }

    [Fact]
    public async Task StageUpdate_ManifestInvalid_Fails()
    {
        var manifestJson = """{"schema_version":99,"version":"0.1.4","tag":"v0.1.4","commit_sha":"74875dfcc6762ec0edb75c40e225150f94fa45e5","asset_name":"BDO-UA-Client-v0.1.4-win-x64.zip","sha256":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2","platform":"win-x64","workflow_run_id":1}""";
        var handler = new QueuedHandler(manifestJson);
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", assets: ManifestAsset());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.ManifestInvalid, result.Error);
    }

    [Fact]
    public async Task StageUpdate_MissingZipAsset_FailsAssetMissing()
    {
        var manifestJson = """{"schema_version":1,"version":"0.1.4","tag":"v0.1.4","commit_sha":"74875dfcc6762ec0edb75c40e225150f94fa45e5","asset_name":"BDO-UA-Client-v0.1.4-win-x64.zip","sha256":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2","platform":"win-x64","workflow_run_id":1}""";
        var handler = new QueuedHandler(manifestJson);
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", assets: ManifestAsset());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_ZipHashMismatch_Fails()
    {
        var validManifestSha = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{validManifestSha}\",\"platform\":\"win-x64\",\"workflow_run_id\":1}}";

        var zipBytes = CreateMinimalZipWithExe("wrong content");

        var handler = new QueuedHandler(manifestJson, zipBytes);
        var service = CreateServiceWithHandler(handler);

        var assets = FullAssets(validManifestSha, zipBytes.Length);
        var candidate = MakeCandidate("v0.1.4", assets: assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.HashMismatch, result.Error);
    }

    [Fact]
    public async Task StageUpdate_Cancelled_ReturnsCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", assets: ManifestAsset());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, cts.Token);
        Assert.False(result.IsSuccess);
        Assert.True(result.Error == UpdatePackageError.Cancelled || result.Error == UpdatePackageError.ManifestDownloadFailed);
    }

    private UpdatePackageService CreateServiceWithHandler(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var gitHubClient = new GitHubUpdateClient(httpClient, _logger);
        var manifestValidator = new UpdateManifestValidator(_logger);
        var sessionStore = new UpdateSessionStore(_appPaths, _logger);
        return new UpdatePackageService(gitHubClient, manifestValidator, sessionStore, _appPaths, _logger);
    }

    private static AppVersionInfo PublicVersionInfo() => AppVersionInfo.FromRawVersion("0.1.3");

    private static UpdateCandidate MakeCandidate(string tag, GitHubReleaseAsset[] assets)
    {
        var version = AppVersion.TryParseReleaseTag(tag)!.Value;
        return new UpdateCandidate(version, tag, new GitHubRelease
        {
            TagName = tag,
            Draft = false,
            Prerelease = false,
            PublishedAt = DateTimeOffset.UtcNow,
            Assets = assets.ToList()
        });
    }

    private static GitHubReleaseAsset[] ManifestAsset() => new[]
    {
        new GitHubReleaseAsset
        {
            Name = "release-manifest.json",
            BrowserDownloadUrl = "https://github.com/test/manifest",
            Size = 500,
            State = "uploaded"
        }
    };

    private static GitHubReleaseAsset[] FullAssets(string sha, int zipSize) => new[]
    {
        new GitHubReleaseAsset
        {
            Name = "release-manifest.json",
            BrowserDownloadUrl = "https://github.com/test/manifest",
            Size = 500,
            State = "uploaded"
        },
        new GitHubReleaseAsset
        {
            Name = "BDO-UA-Client-v0.1.4-win-x64.zip",
            BrowserDownloadUrl = "https://github.com/test/zip",
            Size = zipSize,
            State = "uploaded"
        }
    };

    private static byte[] CreateMinimalZipWithExe(string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("BDO-UA-Client.exe");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }

    private class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new();

        public QueuedHandler(params object[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("No more queued responses")
                });

            var content = _responses.Dequeue();
            HttpResponseMessage response;

            if (content is byte[] bytes)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
                response.Content.Headers.ContentLength = bytes.Length;
            }
            else if (content is HttpStatusCode status)
            {
                response = new HttpResponseMessage(status);
            }
            else
            {
                var str = content?.ToString() ?? "";
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(str, Encoding.UTF8, "application/json")
                };
            }

            return Task.FromResult(response);
        }
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

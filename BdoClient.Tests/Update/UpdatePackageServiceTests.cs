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

    // --- Candidate validation (§7) ---

    [Fact]
    public async Task StageUpdate_NonPublicCurrentVersion_ReturnsInvalidCandidate()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", Array.Empty<GitHubReleaseAsset>());
        var currentInfo = AppVersionInfo.FromRawVersion("0.0.0-dev.abcdef");
        var result = await service.StageUpdateAsync(candidate, currentInfo, null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.InvalidCandidate, result.Error);
        Assert.Contains("not a public release", result.ErrorMessage!);
    }

    [Fact]
    public async Task StageUpdate_CandidateEqualToCurrent_ReturnsInvalidCandidate()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.3", Array.Empty<GitHubReleaseAsset>());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.InvalidCandidate, result.Error);
        Assert.Contains("not newer", result.ErrorMessage!);
    }

    [Fact]
    public async Task StageUpdate_MalformedCandidateTag_ReturnsInvalidCandidate()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = new UpdateCandidate(new AppVersion(0, 1, 4), "0.1.4", new GitHubRelease
        {
            TagName = "0.1.4",
            Draft = false,
            PublishedAt = DateTimeOffset.UtcNow
        });
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.InvalidCandidate, result.Error);
        Assert.Contains("tag does not match", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StageUpdate_DraftCandidate_ReturnsInvalidCandidate()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var version = AppVersion.TryParseReleaseTag("v0.1.4")!.Value;
        var candidate = new UpdateCandidate(version, "v0.1.4", new GitHubRelease
        {
            TagName = "v0.1.4",
            Draft = true,
            PublishedAt = DateTimeOffset.UtcNow
        });
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.InvalidCandidate, result.Error);
        Assert.Contains("draft", result.ErrorMessage!);
    }

    [Fact]
    public async Task StageUpdate_UnpublishedCandidate_ReturnsInvalidCandidate()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var version = AppVersion.TryParseReleaseTag("v0.1.4")!.Value;
        var candidate = new UpdateCandidate(version, "v0.1.4", new GitHubRelease
        {
            TagName = "v0.1.4",
            Draft = false,
            PublishedAt = null
        });
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.InvalidCandidate, result.Error);
        Assert.Contains("not published", result.ErrorMessage!);
    }

    // --- Asset tests ---

    [Fact]
    public async Task StageUpdate_MissingManifestAsset_FailsAndCleansSession()
    {
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", Array.Empty<GitHubReleaseAsset>());
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_ManifestDownloadFails_FailsAndCleansSession()
    {
        var manifestBytes = Encoding.UTF8.GetBytes("{}");
        var handler = new QueuedHandler(HttpStatusCode.ServiceUnavailable);
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", ManifestAsset(manifestBytes.Length));
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.ManifestDownloadFailed, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_ManifestInvalid_FailsAndCleansSession()
    {
        var manifestJson = """{"schema_version":99,"version":"0.1.4","tag":"v0.1.4","commit_sha":"74875dfcc6762ec0edb75c40e225150f94fa45e5","asset_name":"BDO-UA-Client-v0.1.4-win-x64.zip","sha256":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2","platform":"win-x64","workflow_run_id":"1"}""";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes);
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", ManifestAsset(manifestBytes.Length));
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.ManifestInvalid, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_MissingZipAsset_FailsAndCleansSession()
    {
        var manifestJson = """{"schema_version":1,"version":"0.1.4","tag":"v0.1.4","commit_sha":"74875dfcc6762ec0edb75c40e225150f94fa45e5","asset_name":"BDO-UA-Client-v0.1.4-win-x64.zip","sha256":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2","platform":"win-x64","workflow_run_id":"32211040254"}""";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes);
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", ManifestAsset(manifestBytes.Length));
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_ZipHashMismatch_FailsAndCleansSession()
    {
        var validManifestSha = new string('a', 64);
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{validManifestSha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var zipBytes = CreateMinimalZipWithExe("wrong content");
        var handler = new QueuedHandler(manifestBytes, zipBytes);
        var service = CreateServiceWithHandler(handler);
        var assets = FullAssets(manifestBytes.Length, zipBytes.Length);
        var candidate = MakeCandidate("v0.1.4", assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.HashMismatch, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    // --- Duplicate asset (§6) ---

    [Fact]
    public async Task StageUpdate_DuplicateManifestAsset_Fails()
    {
        var candidate = MakeCandidate("v0.1.4", new[]
        {
            new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/a", Size = 100, State = "uploaded" },
            new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/b", Size = 100, State = "uploaded" }
        });
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_DuplicateZipAsset_Fails()
    {
        var validManifestSha = new string('a', 64);
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{validManifestSha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var candidate = MakeCandidate("v0.1.4", new[]
        {
            new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/m", Size = manifestBytes.Length, State = "uploaded" },
            new GitHubReleaseAsset { Name = "BDO-UA-Client-v0.1.4-win-x64.zip", BrowserDownloadUrl = "https://github.com/a", Size = 100, State = "uploaded" },
            new GitHubReleaseAsset { Name = "BDO-UA-Client-v0.1.4-win-x64.zip", BrowserDownloadUrl = "https://github.com/b", Size = 100, State = "uploaded" }
        });
        var handler = new QueuedHandler(manifestBytes);
        var service = CreateServiceWithHandler(handler);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_DuplicateManifestOneMalformed_Fails()
    {
        var candidate = MakeCandidate("v0.1.4", new[]
        {
            new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/a", Size = 100, State = "uploaded" },
            new GitHubReleaseAsset { Name = "release-manifest.json", Size = 0, State = "uploaded" }
        });
        var handler = new QueuedHandler();
        var service = CreateServiceWithHandler(handler);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    // --- Digest tests ---

    [Fact]
    public async Task StageUpdate_MalformedDigest_Fails()
    {
        var sha = new string('a', 64);
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{sha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes);
        var service = CreateServiceWithHandler(handler);
        var assets = new[]
        {
            new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/m", Size = manifestBytes.Length, State = "uploaded" },
            new GitHubReleaseAsset { Name = "BDO-UA-Client-v0.1.4-win-x64.zip", BrowserDownloadUrl = "https://github.com/z", Size = 100, State = "uploaded", Digest = "md5:abc123" }
        };
        var candidate = MakeCandidate("v0.1.4", assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.HashMismatch, result.Error);
    }

    [Fact]
    public async Task StageUpdate_DigestMismatch_Fails()
    {
        var sha = new string('a', 64);
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{sha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes);
        var service = CreateServiceWithHandler(handler);
        var assets = new[]
        {
            new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/m", Size = manifestBytes.Length, State = "uploaded" },
            new GitHubReleaseAsset { Name = "BDO-UA-Client-v0.1.4-win-x64.zip", BrowserDownloadUrl = "https://github.com/z", Size = 100, State = "uploaded", Digest = "sha256:" + new string('b', 64) }
        };
        var candidate = MakeCandidate("v0.1.4", assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.HashMismatch, result.Error);
    }

    // --- Cancellation / cleanup ---

    [Fact]
    public async Task StageUpdate_Cancellation_CleansSession()
    {
        var handler = new CancellationPropagatingHandler();
        var service = CreateServiceWithHandler(handler);
        var candidate = MakeCandidate("v0.1.4", ManifestAsset(100));
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, cts.Token);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.Cancelled, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_DownloadFailure_CleansSession()
    {
        var sha = new string('a', 64);
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{sha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes, HttpStatusCode.NotFound);
        var service = CreateServiceWithHandler(handler);
        var assets = FullAssets(manifestBytes.Length, 100);
        var candidate = MakeCandidate("v0.1.4", assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.DownloadFailed, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_ZipInvalid_CleansSession()
    {
        var invalidZipBytes = Encoding.UTF8.GetBytes("not a zip");
        var invalidZipSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(invalidZipBytes)).ToLowerInvariant();
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{invalidZipSha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes, invalidZipBytes);
        var service = CreateServiceWithHandler(handler);
        var assets = FullAssets(manifestBytes.Length, invalidZipBytes.Length);
        var candidate = MakeCandidate("v0.1.4", assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.PackageInvalid, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_ExeValidationFailure_CleansSession()
    {
        var badExeZip = CreateMinimalZipWithExe("wrong version content");
        var badExeSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(badExeZip)).ToLowerInvariant();
        var manifestJson = $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"sha256\":\"{badExeSha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var handler = new QueuedHandler(manifestBytes, badExeZip);
        var service = CreateServiceWithHandler(handler);
        var assets = FullAssets(manifestBytes.Length, badExeZip.Length);
        var candidate = MakeCandidate("v0.1.4", assets);
        var result = await service.StageUpdateAsync(candidate, PublicVersionInfo(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UpdatePackageError.ExecutableInvalid, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    // --- Helpers ---

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

    private static GitHubReleaseAsset[] ManifestAsset(int manifestSize) => new[]
    {
        new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/test/manifest", Size = manifestSize, State = "uploaded" }
    };

    private static GitHubReleaseAsset[] FullAssets(int manifestSize, int zipSize) => new[]
    {
        new GitHubReleaseAsset { Name = "release-manifest.json", BrowserDownloadUrl = "https://github.com/test/manifest", Size = manifestSize, State = "uploaded" },
        new GitHubReleaseAsset { Name = "BDO-UA-Client-v0.1.4-win-x64.zip", BrowserDownloadUrl = "https://github.com/test/zip", Size = zipSize, State = "uploaded" }
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
        public QueuedHandler(params object[] responses) { foreach (var r in responses) _responses.Enqueue(r); }

        public void Enqueue(object response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("No more responses") });

            var content = _responses.Dequeue();
            HttpResponseMessage response;

            if (content is byte[] bytes)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentLength = bytes.Length;
            }
            else if (content is HttpStatusCode status)
            {
                response = new HttpResponseMessage(status);
            }
            else
            {
                var str = content?.ToString() ?? "";
                var strBytes = Encoding.UTF8.GetBytes(str);
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(strBytes) };
                response.Content.Headers.ContentLength = strBytes.Length;
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            }
            return Task.FromResult(response);
        }
    }

    private class BlockingHttpResponse : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private class CancellationPropagatingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
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

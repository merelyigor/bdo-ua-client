using System.Net;
using System.Security.Cryptography;
using System.Text;
using BdoClient.Logging;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class UpdatePackageServiceTests : IDisposable
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
    public async Task StageUpdate_MissingManifestAsset_CleansSession()
    {
        var result = await CreateService(new QueuedHandler()).StageUpdateAsync(
            Candidate(Array.Empty<GitHubReleaseAsset>()), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_ZipAssetManifest_IsRejectedByValidator()
    {
        var json = ManifestJson("BDO-UA-Client-v0.1.4-win-x64.zip", new string('a', 64));
        var result = await CreateService(new QueuedHandler(Encoding.UTF8.GetBytes(json))).StageUpdateAsync(
            Candidate(new[] { ManifestAsset(json.Length) }), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.ManifestInvalid, result.Error);
    }

    [Fact]
    public async Task StageUpdate_MissingDirectExeAsset_FailsClosed()
    {
        var json = ManifestJson("BDO-UA-Client.exe", new string('a', 64));
        var result = await CreateService(new QueuedHandler(Encoding.UTF8.GetBytes(json))).StageUpdateAsync(
            Candidate(new[] { ManifestAsset(json.Length) }), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_DuplicateDirectExeAssets_FailsClosed()
    {
        var payload = Encoding.UTF8.GetBytes("direct exe payload");
        var sha = Sha(payload);
        var json = ManifestJson("BDO-UA-Client.exe", sha);
        var assets = new[]
        {
            ManifestAsset(json.Length),
            ExeAsset(payload.Length, "https://example.test/a"),
            ExeAsset(payload.Length, "https://example.test/b")
        };

        var result = await CreateService(new QueuedHandler(Encoding.UTF8.GetBytes(json))).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_ZeroSizedDirectExe_FailsClosed()
    {
        var json = ManifestJson("BDO-UA-Client.exe", new string('a', 64));
        var assets = new[] { ManifestAsset(json.Length), ExeAsset(0, "https://example.test/exe") };

        var result = await CreateService(new QueuedHandler(Encoding.UTF8.GetBytes(json))).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_HttpDirectExeUrl_IsRejected()
    {
        var payload = Encoding.UTF8.GetBytes("direct exe payload");
        var json = ManifestJson("BDO-UA-Client.exe", Sha(payload));
        var assets = new[] { ManifestAsset(json.Length), ExeAsset(payload.Length, "http://example.test/exe") };

        var result = await CreateService(new QueuedHandler(Encoding.UTF8.GetBytes(json))).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.AssetMissing, result.Error);
    }

    [Fact]
    public async Task StageUpdate_DirectExeShaMismatch_CleansSession()
    {
        var payload = Encoding.UTF8.GetBytes("direct exe payload");
        var json = ManifestJson("BDO-UA-Client.exe", new string('a', 64));
        var handler = new QueuedHandler(Encoding.UTF8.GetBytes(json), payload);
        var assets = new[] { ManifestAsset(json.Length), ExeAsset(payload.Length, "https://example.test/exe") };

        var result = await CreateService(handler).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.HashMismatch, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
        Assert.DoesNotContain("update-package.zip", Directory.GetFiles(_tempRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StageUpdate_GitHubDigestMismatch_FailsBeforeDownload()
    {
        var payload = Encoding.UTF8.GetBytes("direct exe payload");
        var json = ManifestJson("BDO-UA-Client.exe", Sha(payload));
        var handler = new QueuedHandler(Encoding.UTF8.GetBytes(json));
        var assets = new[] { ManifestAsset(json.Length), ExeAsset(payload.Length, "https://example.test/exe", "sha256:" + new string('b', 64)) };

        var result = await CreateService(handler).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.HashMismatch, result.Error);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task StageUpdate_DirectExeDownloadsToFixedStagingName()
    {
        var payload = Encoding.UTF8.GetBytes("direct exe payload");
        var json = ManifestJson("BDO-UA-Client.exe", Sha(payload));
        var handler = new QueuedHandler(Encoding.UTF8.GetBytes(json), payload);
        var assets = new[] { ManifestAsset(json.Length), ExeAsset(payload.Length, "https://example.test/exe") };

        var result = await CreateService(handler).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.ExecutableInvalid, result.Error);
        Assert.Equal("https://example.test/exe", handler.RequestedUris.Last());
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_Cancellation_CleansSession()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await CreateService(new QueuedHandler()).StageUpdateAsync(
            Candidate(new[] { ManifestAsset(100) }), PublicVersionInfo(), null, cts.Token);

        Assert.Equal(UpdatePackageError.Cancelled, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    [Fact]
    public async Task StageUpdate_DownloadFailure_CleansSession()
    {
        var payload = Encoding.UTF8.GetBytes("direct exe payload");
        var json = ManifestJson("BDO-UA-Client.exe", Sha(payload));
        var handler = new QueuedHandler(Encoding.UTF8.GetBytes(json), HttpStatusCode.NotFound);
        var assets = new[] { ManifestAsset(json.Length), ExeAsset(payload.Length, "https://example.test/exe") };

        var result = await CreateService(handler).StageUpdateAsync(
            Candidate(assets), PublicVersionInfo(), null);

        Assert.Equal(UpdatePackageError.DownloadFailed, result.Error);
        Assert.Empty(Directory.GetDirectories(_appPaths.UpdatesDir));
    }

    private UpdatePackageService CreateService(HttpMessageHandler handler)
    {
        var client = new GitHubUpdateClient(new HttpClient(handler), _logger);
        return new UpdatePackageService(client, new UpdateManifestValidator(_logger),
            new UpdateSessionStore(_appPaths, _logger), _appPaths, _logger);
    }

    private static AppVersionInfo PublicVersionInfo() => AppVersionInfo.FromRawVersion("0.1.3");

    private static UpdateCandidate Candidate(GitHubReleaseAsset[] assets) => new(
        new AppVersion(0, 1, 4), "v0.1.4", new GitHubRelease
        {
            TagName = "v0.1.4",
            Draft = false,
            PublishedAt = DateTimeOffset.UtcNow,
            Assets = assets.ToList()
        });

    private static GitHubReleaseAsset ManifestAsset(int size) => new()
    {
        Name = "release-manifest.json",
        BrowserDownloadUrl = "https://example.test/manifest",
        Size = size,
        State = "uploaded"
    };

    private static GitHubReleaseAsset ExeAsset(long size, string url, string? digest = null) => new()
    {
        Name = "BDO-UA-Client.exe",
        BrowserDownloadUrl = url,
        Size = size,
        State = "uploaded",
        Digest = digest
    };

    private static string ManifestJson(string assetName, string sha) =>
        $"{{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"{assetName}\",\"sha256\":\"{sha}\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}}";

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new();
        public List<string> RequestedUris { get; } = new();
        public int RequestCount => RequestedUris.Count;

        public QueuedHandler(params object[] responses)
        {
            foreach (var response in responses)
                _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.ToString());
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            var value = _responses.Count == 0 ? HttpStatusCode.ServiceUnavailable : _responses.Dequeue();
            if (value is HttpStatusCode status)
                return Task.FromResult(new HttpResponseMessage(status));

            var bytes = value is byte[] raw ? raw : Encoding.UTF8.GetBytes(value.ToString() ?? "");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

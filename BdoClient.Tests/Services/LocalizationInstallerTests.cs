using System.Security.Cryptography;
using System.Text;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Tests.Services;

public class LocalizationInstallerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly NullLogger _logger = new();

    public LocalizationInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ValidateReleaseMetadata

    [Fact]
    public void ValidateReleaseMetadata_Valid_DoesNotThrow()
    {
        var release = CreateValidRelease();
        LocalizationInstaller.ValidateReleaseMetadata(release);
    }

    [Fact]
    public void ValidateReleaseMetadata_MissingDownloadUrl_Throws()
    {
        var release = CreateValidRelease();
        release.DownloadUrl = null;
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    [Fact]
    public void ValidateReleaseMetadata_NonHttps_Throws()
    {
        var release = CreateValidRelease();
        release.DownloadUrl = "http://example.com/file.loc";
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    [Fact]
    public void ValidateReleaseMetadata_ZeroSize_Throws()
    {
        var release = CreateValidRelease();
        release.SizeBytes = 0;
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    [Fact]
    public void ValidateReleaseMetadata_NegativeSize_Throws()
    {
        var release = CreateValidRelease();
        release.SizeBytes = -1;
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    [Fact]
    public void ValidateReleaseMetadata_MissingSha256_Throws()
    {
        var release = CreateValidRelease();
        release.Sha256 = null;
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    [Fact]
    public void ValidateReleaseMetadata_MissingPublicId_Throws()
    {
        var release = CreateValidRelease();
        release.PublicId = null;
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    // ValidateOfficialUrl

    [Fact]
    public void ValidateOfficialUrl_Valid_DoesNotThrow()
    {
        LocalizationInstaller.ValidateOfficialUrl("https://example.com/loc.loc");
    }

    [Fact]
    public void ValidateOfficialUrl_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateOfficialUrl(""));
    }

    [Fact]
    public void ValidateOfficialUrl_Http_Throws()
    {
        Assert.Throws<ArgumentException>(() => LocalizationInstaller.ValidateOfficialUrl("http://example.com/loc.loc"));
    }

    // Successful release download

    [Fact]
    public async Task DownloadReleaseAsync_Success_ReturnsVerifiedFile()
    {
        var content = Encoding.UTF8.GetBytes("test localization content");
        var sha256 = ComputeSha256(content);
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = sha256;

        var result = await installer.DownloadReleaseAsync(release);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.TempFilePath);
        Assert.True(File.Exists(result.TempFilePath));
        Assert.Equal(content.Length, result.SizeBytes);
        Assert.Equal(sha256, result.Sha256);
    }

    // SHA-256 mismatch

    [Fact]
    public async Task DownloadReleaseAsync_WrongSha_ReturnsHashMismatch()
    {
        var content = Encoding.UTF8.GetBytes("test content");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.HashMismatch, result.Error);
    }

    // Size mismatch (Content-Length)

    [Fact]
    public async Task DownloadReleaseAsync_ContentLengthMismatch_ReturnsSizeMismatch()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: 9999);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.SizeMismatch, result.Error);
    }

    // Size mismatch (actual download)

    [Fact]
    public async Task DownloadReleaseAsync_DownloadSizeMismatch_ReturnsSizeMismatch()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: null);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = 9999;
        release.Sha256 = ComputeSha256(content);

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.SizeMismatch, result.Error);
    }

    // HTTP 404 → no retry

    [Fact]
    public async Task DownloadReleaseAsync_404_ReturnsHttpError()
    {
        var handler = new MockHttpHandler(null, statusCode: System.Net.HttpStatusCode.NotFound);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
        Assert.Equal(1, handler.RequestCount);
    }

    // 500 → retry then fail

    [Fact]
    public async Task DownloadReleaseAsync_500_RetriesThenFails()
    {
        var handler = new MockHttpHandler(null, statusCode: System.Net.HttpStatusCode.InternalServerError);
        handler.FailUntilAttempt = 10;
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Network, result.Error);
        Assert.Equal(4, handler.RequestCount);
    }

    // Cancellation

    [Fact]
    public async Task DownloadReleaseAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.DownloadReleaseAsync(release, cancellationToken: cts.Token));
    }

    // Metadata validation prevents HTTP call

    [Fact]
    public async Task DownloadReleaseAsync_InvalidMetadata_NoHttpRequest()
    {
        var handler = new MockHttpHandler(null);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.DownloadUrl = null;

        await Assert.ThrowsAsync<ArgumentException>(
            () => installer.DownloadReleaseAsync(release));
        Assert.Equal(0, handler.RequestCount);
    }

    // Temp file cleaned on failure

    [Fact]
    public async Task DownloadReleaseAsync_HashMismatch_TempFileCleaned()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = "wrong_hash";

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(result.TempFilePath));
    }

    // Temp file remains on success

    [Fact]
    public async Task DownloadReleaseAsync_Success_TempFileRemains()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var sha256 = ComputeSha256(content);
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = sha256;

        var result = await installer.DownloadReleaseAsync(release);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.TempFilePath));
    }

    // Progress reporting

    [Fact]
    public async Task DownloadReleaseAsync_WithProgress_ReportsBytes()
    {
        var content = Encoding.UTF8.GetBytes("test content");
        var sha256 = ComputeSha256(content);
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = sha256;

        var progressReports = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));

        await installer.DownloadReleaseAsync(release, progress);

        Assert.NotEmpty(progressReports);
        Assert.Equal(content.Length, progressReports[^1].BytesDownloaded);
    }

    // SHA-256 case-insensitive

    [Fact]
    public async Task DownloadReleaseAsync_Sha256CaseInsensitive_Success()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var sha256 = ComputeSha256(content).ToUpperInvariant();
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = sha256;

        var result = await installer.DownloadReleaseAsync(release);

        Assert.True(result.IsSuccess);
    }

    // Official source: successful download without SHA

    [Fact]
    public async Task DownloadOfficialSourceAsync_Success_ReturnsFile()
    {
        var content = Encoding.UTF8.GetBytes("official content");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length);
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.TempFilePath);
        Assert.True(File.Exists(result.TempFilePath));
        Assert.Equal(content.Length, result.SizeBytes);
        Assert.Null(result.Sha256);
    }

    // Official source: HTTP failure

    [Fact]
    public async Task DownloadOfficialSourceAsync_HttpFailure_ReturnsError()
    {
        var handler = new MockHttpHandler(null, statusCode: System.Net.HttpStatusCode.NotFound);
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
    }

    // Official source: cancellation

    [Fact]
    public async Task DownloadOfficialSourceAsync_Cancellation_Throws()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, statusCode: System.Net.HttpStatusCode.OK,
            contentLength: content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.DownloadOfficialSourceAsync("https://example.com/loc.loc", cancellationToken: cts.Token));
    }

    // Official source: metadata validation

    [Fact]
    public async Task DownloadOfficialSourceAsync_EmptyUrl_Throws()
    {
        var handler = new MockHttpHandler(null);
        var installer = CreateInstaller(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => installer.DownloadOfficialSourceAsync(""));
    }

    [Fact]
    public async Task DownloadOfficialSourceAsync_HttpUrl_Throws()
    {
        var handler = new MockHttpHandler(null);
        var installer = CreateInstaller(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => installer.DownloadOfficialSourceAsync("http://example.com/loc.loc"));
    }

    // DownloadProgress

    [Fact]
    public void DownloadProgress_Percentage_WithTotal()
    {
        var progress = new DownloadProgress(50, 100);
        Assert.Equal(50.0, progress.Percentage);
    }

    [Fact]
    public void DownloadProgress_Percentage_WithoutTotal()
    {
        var progress = new DownloadProgress(50, null);
        Assert.Null(progress.Percentage);
    }

    // DownloadResult types

    [Fact]
    public void DownloadResult_Success_HasCorrectProperties()
    {
        var result = DownloadResult.Success("/tmp/file.tmp", 1024, "abc123");
        Assert.True(result.IsSuccess);
        Assert.Equal("/tmp/file.tmp", result.TempFilePath);
        Assert.Equal(1024, result.SizeBytes);
        Assert.Equal("abc123", result.Sha256);
    }

    [Fact]
    public void DownloadResult_SuccessWithoutHash_HasCorrectProperties()
    {
        var result = DownloadResult.SuccessWithoutHash("/tmp/file.tmp", 512);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Sha256);
    }

    [Fact]
    public void DownloadResult_Failure_HasCorrectProperties()
    {
        var result = DownloadResult.Failure(DownloadError.Network, "connection refused");
        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Network, result.Error);
        Assert.Equal("connection refused", result.ErrorMessage);
    }

    private LocalizationInstaller CreateInstaller(MockHttpHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new LocalizationInstaller(httpClient, _paths, _logger);
    }

    private static CurrentRelease CreateValidRelease()
    {
        return new CurrentRelease
        {
            PublicId = "0123456789ABCDEF01234567",
            Version = 1,
            DownloadUrl = "https://example.com/release.loc",
            SizeBytes = 100,
            Sha256 = "aabbccdd",
            Patch = 100,
            CompatibleWithOfficialPatch = true
        };
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[]? _responseContent;
        private readonly System.Net.HttpStatusCode _statusCode;
        private readonly long? _contentLength;
        private readonly int _delayMs;

        public int RequestCount { get; private set; }
        public int FailUntilAttempt { get; set; }

        public MockHttpHandler(byte[]? responseContent,
            System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK,
            long? contentLength = null,
            int delayMs = 0)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
            _contentLength = contentLength;
            _delayMs = delayMs;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_delayMs > 0)
                await Task.Delay(_delayMs, cancellationToken);

            if (RequestCount <= FailUntilAttempt)
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);

            if (_responseContent == null)
                return new HttpResponseMessage(_statusCode);

            var content = new ByteArrayContent(_responseContent);
            if (_contentLength.HasValue)
                content.Headers.ContentLength = _contentLength.Value;

            return new HttpResponseMessage(_statusCode) { Content = content };
        }
    }
}

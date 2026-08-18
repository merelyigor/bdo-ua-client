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

    private int GetTmpFileCount()
    {
        return Directory.GetFiles(_paths.CacheDir, "*.tmp").Length;
    }

    // ValidateReleaseMetadata

    [Fact]
    public void ValidateReleaseMetadata_Valid_ReturnsNull()
    {
        var release = CreateValidRelease();
        Assert.Null(LocalizationInstaller.ValidateReleaseMetadata(release));
    }

    [Fact]
    public void ValidateReleaseMetadata_MissingDownloadUrl_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.DownloadUrl = null;
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_HttpUrl_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.DownloadUrl = "http://example.com/file.loc";
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_RelativeUrl_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.DownloadUrl = "/relative/path/file.loc";
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_MalformedUrl_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.DownloadUrl = "https://";
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_ZeroSize_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.SizeBytes = 0;
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_NegativeSize_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.SizeBytes = -1;
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_MissingSha256_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.Sha256 = null;
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateReleaseMetadata_MissingPublicId_ReturnsInvalidMetadata()
    {
        var release = CreateValidRelease();
        release.PublicId = null;
        var result = LocalizationInstaller.ValidateReleaseMetadata(release);
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    // ValidateOfficialUrl

    [Fact]
    public void ValidateOfficialUrl_Valid_ReturnsNull()
    {
        Assert.Null(LocalizationInstaller.ValidateOfficialUrl("https://example.com/loc.loc"));
    }

    [Fact]
    public void ValidateOfficialUrl_Empty_ReturnsInvalidMetadata()
    {
        var result = LocalizationInstaller.ValidateOfficialUrl("");
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateOfficialUrl_Http_ReturnsInvalidMetadata()
    {
        var result = LocalizationInstaller.ValidateOfficialUrl("http://example.com/loc.loc");
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    [Fact]
    public void ValidateOfficialUrl_Malformed_ReturnsInvalidMetadata()
    {
        var result = LocalizationInstaller.ValidateOfficialUrl("not-a-url");
        Assert.NotNull(result);
        Assert.Equal(DownloadError.InvalidMetadata, result!.Error);
    }

    // Successful release download

    [Fact]
    public async Task DownloadReleaseAsync_Success_ReturnsVerifiedFile()
    {
        var content = Encoding.UTF8.GetBytes("test localization content");
        var sha256 = ComputeSha256(content);
        var handler = new MockHttpHandler(content, content.Length);
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
        var handler = new MockHttpHandler(content, content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.HashMismatch, result.Error);
        Assert.Equal(0, GetTmpFileCount());
    }

    // Size mismatch (Content-Length)

    [Fact]
    public async Task DownloadReleaseAsync_ContentLengthMismatch_ReturnsSizeMismatch()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, 9999);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.SizeMismatch, result.Error);
        Assert.Equal(0, GetTmpFileCount());
    }

    // Size mismatch (actual download)

    [Fact]
    public async Task DownloadReleaseAsync_DownloadSizeMismatch_ReturnsSizeMismatch()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, omitContentLength: true);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = 9999;
        release.Sha256 = ComputeSha256(content);

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.SizeMismatch, result.Error);
        Assert.Equal(0, GetTmpFileCount());
    }

    // HTTP 404 → no retry, final Http error

    [Fact]
    public async Task DownloadReleaseAsync_404_NoRetry_ReturnsHttp()
    {
        var handler = new MockHttpHandler(null, 0, statusCode: 404);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
        Assert.Contains("404", result.ErrorMessage);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(0, GetTmpFileCount());
    }

    // HTTP 500 → retry then fail with Http

    [Fact]
    public async Task DownloadReleaseAsync_500_RetriesThenFails_Http()
    {
        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0, 0, 0 });

        var release = CreateValidRelease();

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
        Assert.Contains("500", result.ErrorMessage);
        Assert.Equal(4, handler.RequestCount);
    }

    // HTTP 408 → retry then fail with Http

    [Fact]
    public async Task DownloadReleaseAsync_408_RetriesThenFails_Http()
    {
        var handler = new MockHttpHandler(null, 0, statusCode: 408);
        handler.FailUntilAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0, 0, 0 });

        var release = CreateValidRelease();

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
        Assert.Contains("408", result.ErrorMessage);
        Assert.Equal(4, handler.RequestCount);
    }

    // HttpRequestException → retry then fail with Network

    [Fact]
    public async Task DownloadReleaseAsync_NetworkError_RetriesThenFails_Network()
    {
        var handler = new MockHttpHandler(null, 0);
        handler.ThrowOnAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0, 0, 0 });

        var release = CreateValidRelease();

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Network, result.Error);
        Assert.Equal(4, handler.RequestCount);
    }

    // Internal timeout → retry then fail with Timeout

    [Fact]
    public async Task DownloadReleaseAsync_InternalTimeout_RetriesThenFails_Timeout()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler, timeoutSeconds: 1, retryDelaysMs: new[] { 0, 0, 0 });

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Timeout, result.Error);
        Assert.Equal(4, handler.RequestCount);
    }

    // Caller cancellation → no retry, propagate

    [Fact]
    public async Task DownloadReleaseAsync_CallerCancellation_NoRetry_Throws()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.DownloadReleaseAsync(release, cancellationToken: cts.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    // Metadata validation prevents HTTP call

    [Fact]
    public async Task DownloadReleaseAsync_InvalidMetadata_NoHttpRequest()
    {
        var handler = new MockHttpHandler(null, 0);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.DownloadUrl = null;

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.InvalidMetadata, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // Cleanup after HashMismatch

    [Fact]
    public async Task DownloadReleaseAsync_HashMismatch_CacheDirEmpty()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = "wrong_hash";

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.HashMismatch, result.Error);
        Assert.Equal(0, GetTmpFileCount());
    }

    // Cleanup after SizeMismatch

    [Fact]
    public async Task DownloadReleaseAsync_SizeMismatch_CacheDirEmpty()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, 9999);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.SizeMismatch, result.Error);
        Assert.Equal(0, GetTmpFileCount());
    }

    // Cleanup after cancellation

    [Fact]
    public async Task DownloadReleaseAsync_Cancellation_CacheDirEmpty()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        try
        {
            await installer.DownloadReleaseAsync(release, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.Equal(0, GetTmpFileCount());
    }

    // Stream failure (IOException) after partial write → cleanup

    [Fact]
    public async Task DownloadReleaseAsync_StreamFailure_AfterPartialWrite_CleansTempFile()
    {
        var firstChunk = Encoding.UTF8.GetBytes("first_chunk");
        var handler = new PartialFailingHandler(firstChunk, failOnRead: 1);
        var installer = CreateInstaller(handler);

        var release = CreateValidRelease();
        release.SizeBytes = firstChunk.Length + 500;
        release.Sha256 = "doesnt_matter";

        var result = await installer.DownloadReleaseAsync(release);

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Io, result.Error);
        Assert.Equal(0, GetTmpFileCount());
    }

    // Temp file remains on success

    [Fact]
    public async Task DownloadReleaseAsync_Success_TempFileRemains()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var sha256 = ComputeSha256(content);
        var handler = new MockHttpHandler(content, content.Length);
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
        var handler = new MockHttpHandler(content, content.Length);
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
        var handler = new MockHttpHandler(content, content.Length);
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
        var handler = new MockHttpHandler(content, content.Length);
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.TempFilePath);
        Assert.True(File.Exists(result.TempFilePath));
        Assert.Equal(content.Length, result.SizeBytes);
        Assert.Null(result.Sha256);
    }

    // Official source: 500 → retry then fail

    [Fact]
    public async Task DownloadOfficialSourceAsync_500_RetriesThenFails()
    {
        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0, 0, 0 });

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
        Assert.Equal(4, handler.RequestCount);
    }

    // Official source: network error → retry then fail

    [Fact]
    public async Task DownloadOfficialSourceAsync_NetworkError_RetriesThenFails()
    {
        var handler = new MockHttpHandler(null, 0);
        handler.ThrowOnAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0, 0, 0 });

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Network, result.Error);
        Assert.Equal(4, handler.RequestCount);
    }

    // Official source: internal timeout → retry then fail

    [Fact]
    public async Task DownloadOfficialSourceAsync_Timeout_RetriesThenFails()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler, timeoutSeconds: 1, retryDelaysMs: new[] { 0, 0, 0 });

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Timeout, result.Error);
        Assert.Equal(4, handler.RequestCount);
    }

    // Official source: cancellation → no retry, propagate

    [Fact]
    public async Task DownloadOfficialSourceAsync_Cancellation_Throws()
    {
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.DownloadOfficialSourceAsync("https://example.com/loc.loc", cancellationToken: cts.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    // Official source: 404 → no retry

    [Fact]
    public async Task DownloadOfficialSourceAsync_404_NoRetry()
    {
        var handler = new MockHttpHandler(null, 0, statusCode: 404);
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.Http, result.Error);
        Assert.Equal(1, handler.RequestCount);
    }

    // Official source: metadata validation

    [Fact]
    public async Task DownloadOfficialSourceAsync_EmptyUrl_ReturnsInvalidMetadata()
    {
        var handler = new MockHttpHandler(null, 0);
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadOfficialSourceAsync("");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.InvalidMetadata, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DownloadOfficialSourceAsync_HttpUrl_ReturnsInvalidMetadata()
    {
        var handler = new MockHttpHandler(null, 0);
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadOfficialSourceAsync("http://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(DownloadError.InvalidMetadata, result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    // Official source: temp cleanup on failure

    [Fact]
    public async Task DownloadOfficialSourceAsync_Failure_CacheDirEmpty()
    {
        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0, 0, 0 });

        var result = await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, GetTmpFileCount());
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
    public void DownloadResult_Success_ErrorIsNull()
    {
        var result = DownloadResult.Success("/tmp/file.tmp", 1024, "abc123");
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal("/tmp/file.tmp", result.TempFilePath);
        Assert.Equal(1024, result.SizeBytes);
        Assert.Equal("abc123", result.Sha256);
    }

    [Fact]
    public void DownloadResult_SuccessWithoutHash_ErrorIsNull()
    {
        var result = DownloadResult.SuccessWithoutHash("/tmp/file.tmp", 512);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.Sha256);
    }

    [Fact]
    public void DownloadResult_Failure_ErrorIsNotNull()
    {
        var result = DownloadResult.Failure(DownloadError.Network, "connection refused");
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(DownloadError.Network, result.Error);
        Assert.Equal("connection refused", result.ErrorMessage);
    }

    // Download timing: release network failure

    [Fact]
    public async Task DownloadReleaseAsync_NetworkFailure_TimingContainsTotalMsAndError()
    {
        var logger = new RecordingLogger();
        var handler = new MockHttpHandler(null, 0);
        handler.ThrowOnAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0 }, logger: logger);

        var release = CreateValidRelease();
        await installer.DownloadReleaseAsync(release);

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("Release download timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("total_ms=", timingLine);
        Assert.Contains("error=Network", timingLine);
    }

    // Download timing: release timeout failure

    [Fact]
    public async Task DownloadReleaseAsync_Timeout_TimingContainsTotalMsAndError()
    {
        var logger = new RecordingLogger();
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler, timeoutSeconds: 1, retryDelaysMs: new[] { 0 }, logger: logger);

        var release = CreateValidRelease();
        release.SizeBytes = content.Length;
        release.Sha256 = ComputeSha256(content);

        await installer.DownloadReleaseAsync(release);

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("Release download timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("total_ms=", timingLine);
        Assert.Contains("error=Timeout", timingLine);
    }

    // Download timing: release HTTP failure uses stable error=Http

    [Fact]
    public async Task DownloadReleaseAsync_Http500_TimingUsesStableCategory()
    {
        var logger = new RecordingLogger();
        var handler = new MockHttpHandler(null, 0, statusCode: 500);
        handler.FailUntilAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0 }, logger: logger);

        var release = CreateValidRelease();
        await installer.DownloadReleaseAsync(release);

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("Release download timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("error=Http", timingLine);
        Assert.DoesNotContain("error=Http500", timingLine);
        Assert.Contains("status=500", timingLine);
    }

    // Download timing: official network failure

    [Fact]
    public async Task DownloadOfficialSourceAsync_NetworkFailure_TimingContainsTotalMsAndError()
    {
        var logger = new RecordingLogger();
        var handler = new MockHttpHandler(null, 0);
        handler.ThrowOnAttempt = 10;
        var installer = CreateInstaller(handler, retryDelaysMs: new[] { 0 }, logger: logger);

        await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("Official download timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("total_ms=", timingLine);
        Assert.Contains("error=Network", timingLine);
    }

    // Download timing: official timeout failure

    [Fact]
    public async Task DownloadOfficialSourceAsync_Timeout_TimingContainsTotalMsAndError()
    {
        var logger = new RecordingLogger();
        var content = Encoding.UTF8.GetBytes("test");
        var handler = new MockHttpHandler(content, content.Length, delayMs: 5000);
        var installer = CreateInstaller(handler, timeoutSeconds: 1, retryDelaysMs: new[] { 0 }, logger: logger);

        await installer.DownloadOfficialSourceAsync("https://example.com/loc.loc");

        var timingLine = logger.DebugLines.FirstOrDefault(l => l.Contains("Official download timing:") && l.Contains("error="));
        Assert.NotNull(timingLine);
        Assert.Contains("total_ms=", timingLine);
        Assert.Contains("error=Timeout", timingLine);
    }

    private LocalizationInstaller CreateInstaller(HttpMessageHandler handler,
        int timeoutSeconds = 60, int[]? retryDelaysMs = null, ILogger? logger = null)
    {
        var httpClient = new HttpClient(handler);
        return new LocalizationInstaller(httpClient, _paths, logger: logger ?? _logger,
            timeoutSeconds: timeoutSeconds, retryDelaysMs: retryDelaysMs);
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

    private class RecordingLogger : ILogger
    {
        public List<string> DebugLines { get; } = new();
        public List<string> InfoLines { get; } = new();
        public List<string> WarningLines { get; } = new();
        public List<string> ErrorLines { get; } = new();

        public void Debug(string message) => DebugLines.Add(message);
        public void Info(string message) => InfoLines.Add(message);
        public void Warning(string message) => WarningLines.Add(message);
        public void Error(string message) => ErrorLines.Add(message);
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[]? _responseContent;
        private readonly long _contentLength;
        private readonly int _statusCode;
        private readonly bool _omitContentLength;
        private readonly int _delayMs;

        public int RequestCount { get; private set; }
        public int FailUntilAttempt { get; set; }
        public int ThrowOnAttempt { get; set; }

        public MockHttpHandler(byte[]? responseContent, long contentLength,
            int statusCode = 200, bool omitContentLength = false, int delayMs = 0)
        {
            _responseContent = responseContent;
            _contentLength = contentLength;
            _statusCode = statusCode;
            _omitContentLength = omitContentLength;
            _delayMs = delayMs;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (ThrowOnAttempt > 0 && RequestCount <= ThrowOnAttempt)
                throw new HttpRequestException("Simulated network error");

            if (_delayMs > 0)
                await Task.Delay(_delayMs, cancellationToken);

            if (FailUntilAttempt > 0 && RequestCount <= FailUntilAttempt)
                return new HttpResponseMessage((System.Net.HttpStatusCode)_statusCode);

            if (_responseContent == null)
                return new HttpResponseMessage((System.Net.HttpStatusCode)_statusCode);

            var content = new ByteArrayContent(_responseContent);
            if (!_omitContentLength)
                content.Headers.ContentLength = _contentLength;

            return new HttpResponseMessage((System.Net.HttpStatusCode)_statusCode) { Content = content };
        }
    }

    private class PartialFailingHandler : HttpMessageHandler
    {
        private readonly byte[] _firstChunk;
        private readonly int _failOnRead;

        public int RequestCount { get; private set; }

        public PartialFailingHandler(byte[] firstChunk, int failOnRead = 2)
        {
            _firstChunk = firstChunk;
            _failOnRead = failOnRead;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            var stream = new FailingStream(_firstChunk, _failOnRead);
            var content = new StreamContent(stream);
            content.Headers.ContentLength = stream.Length;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    private class FailingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _failAfterReads;
        private int _reads;
        private int _position;

        public FailingStream(byte[] data, int failAfterReads)
        {
            _data = new byte[data.Length + 500];
            Array.Copy(data, _data, data.Length);
            _failAfterReads = failAfterReads;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var temp = new byte[buffer.Length];
            var read = Read(temp, 0, buffer.Length);
            temp.AsMemory(0, read).CopyTo(buffer);
            return ValueTask.FromResult(read);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _reads++;
            if (_reads > _failAfterReads)
                throw new IOException("Simulated stream failure");

            var available = _data.Length - _position;
            if (available <= 0)
                return 0;

            var toCopy = Math.Min(count, available);
            Array.Copy(_data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing) { }
    }
}

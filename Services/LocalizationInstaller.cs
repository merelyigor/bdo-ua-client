using System.Security.Cryptography;
using System.Text;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class LocalizationInstaller
{
    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysMs = { 1000, 2000, 4000 };
    private const int DefaultTimeoutSeconds = 60;
    private const int ReadBufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;

    public LocalizationInstaller(HttpClient httpClient, AppPaths appPaths, ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LocalizationInstaller(AppPaths appPaths, ILogger logger)
        : this(CreateDefaultHttpClient(), appPaths, logger) { }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler();
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
        };
    }

    public async Task<DownloadResult> DownloadReleaseAsync(
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReleaseMetadata(release);

        var downloadUrl = release.DownloadUrl!;
        var expectedSize = release.SizeBytes;
        var expectedSha256 = release.Sha256!;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                _logger.Info($"Retry attempt {attempt}/{MaxRetries} for {downloadUrl}");
                await DelayWithCancellation(RetryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
            }

            var tempFilePath = CreateTempFilePath();
            Stream? responseStream = null;
            FileStream? fileStream = null;

            try
            {
                _logger.Info($"Download attempt {attempt + 1}: {downloadUrl}");

                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var isRetryable = statusCode == 408 || statusCode >= 500;

                    if (!isRetryable)
                    {
                        _logger.Warning($"HTTP {statusCode} from {downloadUrl} (not retryable)");
                        CleanupTempFile(tempFilePath);
                        return DownloadResult.Failure(DownloadError.Http, $"HTTP {statusCode}");
                    }

                    _logger.Warning($"HTTP {statusCode} from {downloadUrl} (retryable)");
                    CleanupTempFile(tempFilePath);
                    continue;
                }

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value != expectedSize)
                {
                    _logger.Warning($"Content-Length {contentLength.Value} != expected {expectedSize}");
                    CleanupTempFile(tempFilePath);
                    return DownloadResult.Failure(DownloadError.SizeMismatch,
                        $"Content-Length {contentLength.Value} differs from expected {expectedSize}");
                }

                responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
                    ReadBufferSize, FileOptions.Asynchronous);

                var (bytesDownloaded, computedSha256) = await CopyAndHashAsync(
                    responseStream, fileStream, expectedSize, progress, cancellationToken).ConfigureAwait(false);

                if (bytesDownloaded != expectedSize)
                {
                    _logger.Warning($"Downloaded {bytesDownloaded} bytes != expected {expectedSize}");
                    CleanupTempFile(tempFilePath);
                    return DownloadResult.Failure(DownloadError.SizeMismatch,
                        $"Downloaded {bytesDownloaded} bytes differs from expected {expectedSize}");
                }

                if (!string.Equals(computedSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning($"SHA-256 mismatch: computed {computedSha256} != expected {expectedSha256}");
                    CleanupTempFile(tempFilePath);
                    return DownloadResult.Failure(DownloadError.HashMismatch,
                        $"Computed SHA-256 {computedSha256} differs from expected {expectedSha256}");
                }

                _logger.Info($"Verified download: {tempFilePath} ({bytesDownloaded} bytes, SHA-256 OK)");
                return DownloadResult.Success(tempFilePath, bytesDownloaded, computedSha256);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CleanupTempFile(tempFilePath);
                throw;
            }
            catch (TaskCanceledException)
            {
                _logger.Warning($"Timeout on attempt {attempt + 1} for {downloadUrl}");
                CleanupTempFile(tempFilePath);
                continue;
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning($"Network error on attempt {attempt + 1}: {ex.Message}");
                CleanupTempFile(tempFilePath);
                continue;
            }
            catch (IOException ex)
            {
                _logger.Warning($"IO error on attempt {attempt + 1}: {ex.Message}");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Io, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Unexpected error on attempt {attempt + 1}: {ex.Message}");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Unexpected, ex.Message);
            }
            finally
            {
                responseStream?.Dispose();
                fileStream?.Dispose();
            }
        }

        _logger.Error($"Download failed after {MaxRetries + 1} attempts: {downloadUrl}");
        return DownloadResult.Failure(DownloadError.Network, $"Failed after {MaxRetries + 1} attempts");
    }

    public async Task<DownloadResult> DownloadOfficialSourceAsync(
        string officialUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOfficialUrl(officialUrl);

        var tempFilePath = CreateTempFilePath();
        Stream? responseStream = null;
        FileStream? fileStream = null;

        try
        {
            _logger.Info($"Official source download: {officialUrl}");

            using var request = new HttpRequestMessage(HttpMethod.Get, officialUrl);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning($"HTTP {(int)response.StatusCode} from official source");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Http,
                    $"HTTP {(int)response.StatusCode} from official source");
            }

            var contentLength = response.Content.Headers.ContentLength;

            responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
                ReadBufferSize, FileOptions.Asynchronous);

            var bytesDownloaded = await CopyAndReportAsync(
                responseStream, fileStream, contentLength, progress, cancellationToken).ConfigureAwait(false);

            _logger.Info($"Official source downloaded: {tempFilePath} ({bytesDownloaded} bytes)");
            return DownloadResult.SuccessWithoutHash(tempFilePath, bytesDownloaded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupTempFile(tempFilePath);
            throw;
        }
        catch (TaskCanceledException)
        {
            _logger.Warning($"Official source timeout: {officialUrl}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Timeout, "Official source download timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning($"Official source network error: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Network, ex.Message);
        }
        catch (IOException ex)
        {
            _logger.Warning($"Official source IO error: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Io, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Official source unexpected error: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Unexpected, ex.Message);
        }
        finally
        {
            responseStream?.Dispose();
            fileStream?.Dispose();
        }
    }

    internal static void ValidateReleaseMetadata(CurrentRelease release)
    {
        if (string.IsNullOrEmpty(release.DownloadUrl))
            throw new ArgumentException("DownloadUrl is required", nameof(release));

        if (!release.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DownloadUrl must be HTTPS", nameof(release));

        if (release.SizeBytes <= 0)
            throw new ArgumentException("SizeBytes must be positive", nameof(release));

        if (string.IsNullOrEmpty(release.Sha256))
            throw new ArgumentException("Sha256 is required for release downloads", nameof(release));

        if (string.IsNullOrEmpty(release.PublicId))
            throw new ArgumentException("PublicId is required", nameof(release));
    }

    internal static void ValidateOfficialUrl(string officialUrl)
    {
        if (string.IsNullOrEmpty(officialUrl))
            throw new ArgumentException("Official URL is required", nameof(officialUrl));

        if (!officialUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Official URL must be HTTPS", nameof(officialUrl));
    }

    private string CreateTempFilePath()
    {
        var fileName = $"{Guid.NewGuid():N}.tmp";
        return Path.Combine(_appPaths.CacheDir, fileName);
    }

    private static void CleanupTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static async Task DelayWithCancellation(int milliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private async Task<(long bytesDownloaded, string sha256)> CopyAndHashAsync(
        Stream source, Stream destination, long expectedSize,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var hashAlgorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ReadBufferSize];
        long totalBytesRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) break;

            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            hashAlgorithm.AppendData(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;

            progress?.Report(new DownloadProgress(totalBytesRead, expectedSize));
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        var hashBytes = hashAlgorithm.GetHashAndReset();
        var sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (totalBytesRead, sha256);
    }

    private async Task<long> CopyAndReportAsync(
        Stream source, Stream destination, long? totalBytes,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadBufferSize];
        long totalBytesRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) break;

            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            totalBytesRead += bytesRead;

            progress?.Report(new DownloadProgress(totalBytesRead, totalBytes));
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return totalBytesRead;
    }
}

public sealed class DownloadProgress
{
    public long BytesDownloaded { get; }
    public long? TotalBytes { get; }

    public DownloadProgress(long bytesDownloaded, long? totalBytes)
    {
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
    }

    public double? Percentage => TotalBytes.HasValue && TotalBytes.Value > 0
        ? (double)BytesDownloaded / TotalBytes.Value * 100
        : null;
}

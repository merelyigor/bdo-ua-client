using System.Security.Cryptography;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class LocalizationInstaller
{
    private static readonly int[] DefaultRetryDelaysMs = { 1000, 2000, 4000 };
    private const int DefaultTimeoutSeconds = 60;
    private const int ReadBufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;
    private readonly int _timeoutSeconds;
    private readonly int[] _retryDelaysMs;

    public LocalizationInstaller(HttpClient httpClient, AppPaths appPaths, ILogger logger,
        int timeoutSeconds = DefaultTimeoutSeconds, int[]? retryDelaysMs = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
        _retryDelaysMs = retryDelaysMs ?? DefaultRetryDelaysMs;
    }

    public LocalizationInstaller(AppPaths appPaths, ILogger logger,
        int timeoutSeconds = DefaultTimeoutSeconds, int[]? retryDelaysMs = null)
        : this(new HttpClient(), appPaths, logger, timeoutSeconds, retryDelaysMs) { }

    public async Task<DownloadResult> DownloadReleaseAsync(
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateReleaseMetadata(release);
        if (validationError != null)
            return validationError;

        var downloadUrl = release.DownloadUrl!;
        var expectedSize = release.SizeBytes;
        var expectedSha256 = release.Sha256!;

        DownloadError lastError = default;
        string? lastErrorMessage = null;

        for (var attempt = 0; attempt <= _retryDelaysMs.Length; attempt++)
        {
            if (attempt > 0)
            {
                _logger.Info($"Retry attempt {attempt}/{_retryDelaysMs.Length} for {downloadUrl}");
                await DelayWithCancellation(_retryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
            }

            var result = await ExecuteReleaseDownloadAsync(
                downloadUrl, expectedSize, expectedSha256, progress, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
                return result;

            lastError = result.Error!.Value;
            lastErrorMessage = result.ErrorMessage;

            if (!result.IsRetryable)
                return result;
        }

        _logger.Error($"Download failed after {_retryDelaysMs.Length + 1} attempts: {downloadUrl}");
        return DownloadResult.Failure(lastError, lastErrorMessage ?? $"Failed after {_retryDelaysMs.Length + 1} attempts");
    }

    public async Task<DownloadResult> DownloadOfficialSourceAsync(
        string officialUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateOfficialUrl(officialUrl);
        if (validationError != null)
            return validationError;

        DownloadError lastError = default;
        string? lastErrorMessage = null;

        for (var attempt = 0; attempt <= _retryDelaysMs.Length; attempt++)
        {
            if (attempt > 0)
            {
                _logger.Info($"Retry attempt {attempt}/{_retryDelaysMs.Length} for official source");
                await DelayWithCancellation(_retryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
            }

            var result = await ExecuteOfficialDownloadAsync(
                officialUrl, progress, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
                return result;

            lastError = result.Error!.Value;
            lastErrorMessage = result.ErrorMessage;

            if (!result.IsRetryable)
                return result;
        }

        _logger.Error($"Official download failed after {_retryDelaysMs.Length + 1} attempts: {officialUrl}");
        return DownloadResult.Failure(lastError, lastErrorMessage ?? $"Failed after {_retryDelaysMs.Length + 1} attempts");
    }

    private async Task<DownloadResult> ExecuteReleaseDownloadAsync(
        string downloadUrl, long expectedSize, string expectedSha256,
        IProgress<DownloadProgress>? progress, CancellationToken callerToken)
    {
        var tempFilePath = CreateTempFilePath();

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            _logger.Debug($"Download attempt: {downloadUrl} (timeout={_timeoutSeconds}s)");

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var isRetryable = statusCode == 408 || statusCode >= 500;

                _logger.Warning($"HTTP {statusCode} from {downloadUrl}{(isRetryable ? " (retryable)" : "")}");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Http, $"HTTP {statusCode}", isRetryable);
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value != expectedSize)
            {
                _logger.Warning($"Content-Length {contentLength.Value} != expected {expectedSize}");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.SizeMismatch,
                    $"Content-Length {contentLength.Value} differs from expected {expectedSize}");
            }

            long bytesDownloaded;
            string computedSha256;

            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
                    ReadBufferSize, FileOptions.Asynchronous);

                (bytesDownloaded, computedSha256) = await CopyAndHashAsync(
                    responseStream, fileStream, expectedSize, progress, attemptCts.Token).ConfigureAwait(false);
            }

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
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _logger.Info("Download cancelled by caller");
            CleanupTempFile(tempFilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Timeout on download attempt: {downloadUrl}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Timeout, $"Download timed out after {_timeoutSeconds}s", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning($"Network error on download attempt: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Network, ex.Message, isRetryable: true);
        }
        catch (IOException ex)
        {
            _logger.Warning($"IO error on download attempt: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Io, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Unexpected error on download attempt: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Unexpected, ex.Message);
        }
    }

    private async Task<DownloadResult> ExecuteOfficialDownloadAsync(
        string officialUrl, IProgress<DownloadProgress>? progress, CancellationToken callerToken)
    {
        var tempFilePath = CreateTempFilePath();

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            _logger.Debug($"Official download attempt: {officialUrl} (timeout={_timeoutSeconds}s)");

            using var request = new HttpRequestMessage(HttpMethod.Get, officialUrl);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var isRetryable = statusCode == 408 || statusCode >= 500;
                _logger.Warning($"HTTP {statusCode} from official source{(isRetryable ? " (retryable)" : "")}");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Http, $"HTTP {statusCode}", isRetryable);
            }

            var contentLength = response.Content.Headers.ContentLength;

            long bytesDownloaded;

            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
                    ReadBufferSize, FileOptions.Asynchronous);

                bytesDownloaded = await CopyAndReportAsync(
                    responseStream, fileStream, contentLength, progress, attemptCts.Token).ConfigureAwait(false);
            }

            _logger.Info($"Official source downloaded: {tempFilePath} ({bytesDownloaded} bytes)");
            return DownloadResult.SuccessWithoutHash(tempFilePath, bytesDownloaded);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _logger.Info("Official download cancelled by caller");
            CleanupTempFile(tempFilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Timeout on official download attempt: {officialUrl}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Timeout, $"Official download timed out after {_timeoutSeconds}s", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning($"Network error on official download attempt: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Network, ex.Message, isRetryable: true);
        }
        catch (IOException ex)
        {
            _logger.Warning($"IO error on official download attempt: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Io, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Unexpected error on official download attempt: {ex.Message}");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Unexpected, ex.Message);
        }
    }

    internal static DownloadResult? ValidateReleaseMetadata(CurrentRelease release)
    {
        if (string.IsNullOrEmpty(release.DownloadUrl))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "DownloadUrl is required");

        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "DownloadUrl must be a valid HTTPS URL");
        }

        if (release.SizeBytes <= 0)
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "SizeBytes must be positive");

        if (string.IsNullOrEmpty(release.Sha256))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "Sha256 is required for release downloads");

        if (string.IsNullOrEmpty(release.PublicId))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "PublicId is required");

        return null;
    }

    internal static DownloadResult? ValidateOfficialUrl(string officialUrl)
    {
        if (string.IsNullOrEmpty(officialUrl))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "Official URL is required");

        if (!Uri.TryCreate(officialUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "Official URL must be a valid HTTPS URL");
        }

        return null;
    }

    private string CreateTempFilePath()
    {
        var fileName = $"{Guid.NewGuid():N}.tmp";
        return Path.Combine(_appPaths.CacheDir, fileName);
    }

    private void CleanupTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup temp file {path}: {ex.Message}");
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

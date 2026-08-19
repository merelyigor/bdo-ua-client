using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BdoClient.Logging;

namespace BdoClient.Update;

public sealed class GitHubUpdateClient
{
    private const string ReleasesUrl = "https://api.github.com/repos/merelyigor/bdo-ua-client/releases?per_page=100";
    private const string UserAgent = "BDO-UA-Client";
    private const string ApiVersion = "2022-11-28";
    private const int DiscoveryTimeoutSeconds = 15;
    private const int ManifestMaxBytes = 65536;
    public const long ZipMaxBytes = 250_000_000;
    public const long ExeMaxBytes = 200_000_000;
    public const int DownloadTimeoutSeconds = 300;
    public const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public GitHubUpdateClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GitHubResult<List<GitHubRelease>>> FetchReleasesAsync(CancellationToken cancellationToken = default)
    {
        _logger.Debug("GitHub update: fetching releases");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DiscoveryTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var error = $"GitHub HTTP {statusCode}";
                _logger.Warning($"GitHub update: {error}");
                return GitHubResult<List<GitHubRelease>>.Failure(error);
            }

            var content = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            List<GitHubRelease>? releases;
            try
            {
                releases = JsonSerializer.Deserialize<List<GitHubRelease>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.Warning($"GitHub update: JSON error: {ex.Message}");
                return GitHubResult<List<GitHubRelease>>.Failure($"JSON error: {ex.Message}");
            }

            sw.Stop();
            _logger.Debug($"GitHub update: fetched {releases?.Count ?? 0} releases in {sw.ElapsedMilliseconds}ms");

            if (releases == null)
                return GitHubResult<List<GitHubRelease>>.Failure("Deserialized null");

            return GitHubResult<List<GitHubRelease>>.Success(releases);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: timeout after {DiscoveryTimeoutSeconds}s");
            return GitHubResult<List<GitHubRelease>>.Failure("Timeout");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.Debug("GitHub update: cancelled");
            return GitHubResult<List<GitHubRelease>>.Failure("Cancelled");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: network error: {ex.Message}");
            return GitHubResult<List<GitHubRelease>>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error($"GitHub update: unexpected error: {ex.Message}");
            return GitHubResult<List<GitHubRelease>>.Failure($"Unexpected: {ex.Message}");
        }
    }

    public async Task<GitHubResult<UpdateManifest>> FetchManifestAsync(
        GitHubReleaseAsset manifestAsset, CancellationToken cancellationToken = default)
    {
        _logger.Debug("GitHub update: fetching manifest");

        if (manifestAsset.BrowserDownloadUrl == null ||
            !Uri.TryCreate(manifestAsset.BrowserDownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https")
        {
            return GitHubResult<UpdateManifest>.Failure("Invalid manifest URL");
        }

        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return GitHubResult<UpdateManifest>.Failure($"Manifest HTTP {(int)response.StatusCode}");

            var contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (contentBytes.Length > ManifestMaxBytes)
                return GitHubResult<UpdateManifest>.Failure("Manifest too large");

            var content = System.Text.Encoding.UTF8.GetString(contentBytes);

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            sw.Stop();
            _logger.Debug($"GitHub update: manifest fetched in {sw.ElapsedMilliseconds}ms ({contentBytes.Length} bytes)");

            if (manifest == null)
                return GitHubResult<UpdateManifest>.Failure("Manifest deserialized to null");

            return GitHubResult<UpdateManifest>.Success(manifest);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.Debug("GitHub update: manifest cancelled");
            return GitHubResult<UpdateManifest>.Failure("Cancelled");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: manifest network error: {ex.Message}");
            return GitHubResult<UpdateManifest>.Failure($"Network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: manifest JSON error: {ex.Message}");
            return GitHubResult<UpdateManifest>.Failure($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error($"GitHub update: manifest unexpected error: {ex.Message}");
            return GitHubResult<UpdateManifest>.Failure($"Unexpected: {ex.Message}");
        }
    }

    public async Task<GitHubResult<long>> DownloadAssetAsync(
        string downloadUrl,
        string destinationPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug($"GitHub update: downloading asset to {destinationPath}");

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return GitHubResult<long>.Failure("Invalid download URL");

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.Debug($"GitHub update: retry {attempt}/{MaxRetries} after {delay.TotalSeconds}s");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await DownloadAssetCoreAsync(uri, destinationPath, expectedSize, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (IsRetryable(ex))
            {
                _logger.Warning($"GitHub update: retryable download error: {ex.Message}");
                if (attempt == MaxRetries)
                    return GitHubResult<long>.Failure($"Download failed after {MaxRetries + 1} attempts: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning($"GitHub update: non-retryable download error: {ex.Message}");
                return GitHubResult<long>.Failure($"Download failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"GitHub update: download unexpected error: {ex.Message}");
                return GitHubResult<long>.Failure($"Download failed: {ex.Message}");
            }
        }

        return GitHubResult<long>.Failure("Download failed: exhausted retries");
    }

    private async Task<GitHubResult<long>> DownloadAssetCoreAsync(
        Uri uri,
        string destinationPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");

        long? contentLength = response.Content.Headers.ContentLength;

        if (expectedSize > 0 && contentLength.HasValue && contentLength.Value != expectedSize)
            throw new InvalidOperationException($"Content-Length {contentLength.Value} != expected {expectedSize}");

        long maxBytes = expectedSize > 0 ? expectedSize : ZipMaxBytes;

        var sw = Stopwatch.StartNew();
        long totalBytes = 0;
        var buffer = new byte[81920];

        await using var contentStream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, linkedCts.Token).ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
                throw new InvalidOperationException($"Download exceeded max size ({maxBytes} bytes)");

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), linkedCts.Token).ConfigureAwait(false);

            if (progress != null && expectedSize > 0)
                progress.Report((double)totalBytes / expectedSize * 100);
        }

        await fileStream.FlushAsync(linkedCts.Token).ConfigureAwait(false);

        sw.Stop();
        _logger.Debug($"GitHub update: downloaded {totalBytes} bytes in {sw.ElapsedMilliseconds}ms");

        if (expectedSize > 0 && totalBytes != expectedSize)
            throw new InvalidOperationException($"Downloaded {totalBytes} bytes != expected {expectedSize}");

        return GitHubResult<long>.Success(totalBytes);
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        if (ex.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            return true;
        if (ex.StatusCode is >= HttpStatusCode.InternalServerError)
            return true;
        return false;
    }
}

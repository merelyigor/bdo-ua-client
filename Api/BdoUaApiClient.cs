using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Api;

public sealed class BdoUaApiClient
{
    private const string BaseUrl = "https://bdo-ua.com.ua/api/public/v1";
    private const int DefaultTimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly int _timeoutSeconds;

    public BdoUaApiClient(HttpClient httpClient, ILogger logger, int timeoutSeconds = DefaultTimeoutSeconds)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeoutSeconds = timeoutSeconds;
    }

    /// <summary>
    /// Lightweight HEAD request to pre-warm DNS/TLS cache. Fire-and-forget at startup.
    /// </summary>
    public async Task WarmupConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var sw = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://bdo-ua.com.ua/");
            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            sw.Stop();

            _logger.Debug($"Connection warmup completed in {sw.ElapsedMilliseconds}ms (status {(int)response.StatusCode})");
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Connection warmup cancelled/timed out");
        }
        catch (Exception ex)
        {
            _logger.Debug($"Connection warmup failed: {ex.Message}");
        }
    }

    public async Task<ApiResult<ReleasesResponse>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/releases";
        _logger.Debug($"Fetching releases from {url}");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var totalSw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);

            var headersMs = totalSw.ElapsedMilliseconds;

            LogCorrelationHeaders(response);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var error = $"HTTP {statusCode} {response.ReasonPhrase}";
                _logger.Warning($"API error: {error}");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={statusCode} headers_ms={headersMs} total_ms={totalSw.ElapsedMilliseconds} error=Http");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Http, error);
            }

            var contentBytes = await response.Content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
            var bodyMs = totalSw.ElapsedMilliseconds - headersMs;

            if (contentBytes.Length == 0)
            {
                _logger.Warning("Empty API response");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} total_ms={totalSw.ElapsedMilliseconds} bytes=0");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "Empty API response");
            }

            var content = Encoding.UTF8.GetString(contentBytes);

            var parseSw = Stopwatch.StartNew();
            ReleasesResponse? releases;
            try
            {
                releases = JsonSerializer.Deserialize<ReleasesResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                parseSw.Stop();
                _logger.Error($"JSON error: {ex.Message}");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={contentBytes.Length}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, $"JSON error: {ex.Message}");
            }
            parseSw.Stop();

            totalSw.Stop();
            _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} http={response.Version} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={contentBytes.Length}");

            if (releases == null)
            {
                _logger.Warning("Failed to deserialize API response");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "Failed to deserialize API response");
            }

            if (!releases.Success)
            {
                _logger.Warning("API response indicates failure");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "API response success=false");
            }

            if (releases.Data == null)
            {
                _logger.Warning("API response data is null");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "API response data=null");
            }

            _logger.Debug($"Successfully fetched releases: {releases.Data.Modes?.Count ?? 0} modes");
            return ApiResult<ReleasesResponse>.Success(releases);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.Warning($"API request timed out after {_timeoutSeconds}s");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Timeout");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, $"Request timed out after {_timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("API request cancelled");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Cancelled");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Cancelled, "Request cancelled");
        }
        catch (HttpRequestException ex)
        {
            var diag = NetworkDiagnostics.FormatNetworkError(ex);
            _logger.Error($"API network error: {diag}");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Network");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error: {ex.Message}");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Unexpected");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Unexpected, $"Unexpected error: {ex.Message}");
        }
    }

    private void LogCorrelationHeaders(HttpResponseMessage response)
    {
        var parts = new List<string>();

        if (response.Headers.TryGetValues("X-Request-ID", out var requestIdValues))
        {
            var value = requestIdValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                parts.Add($"request_id={value}");
        }

        if (response.Headers.TryGetValues("Server-Timing", out var serverTimingValues))
        {
            var value = serverTimingValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                parts.Add($"server_timing=\"{value}\"");
        }

        if (response.Headers.TryGetValues("CF-Ray", out var cfRayValues))
        {
            var value = cfRayValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                parts.Add($"cf_ray={value}");
        }

        if (parts.Count > 0)
            _logger.Debug($"API correlation: {string.Join(" ", parts)}");
    }
}

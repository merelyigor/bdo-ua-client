using System.Diagnostics;
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

    public async Task<ApiResult<ReleasesResponse>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/releases";
        _logger.Debug($"Fetching releases from {url}");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var totalSw = Stopwatch.StartNew();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);

            var headersMs = totalSw.ElapsedMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.Warning($"API error: {error}");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} total_ms={totalSw.ElapsedMilliseconds}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Http, error);
            }

            var content = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            var bodyMs = totalSw.ElapsedMilliseconds - headersMs;

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.Warning("Empty API response");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} total_ms={totalSw.ElapsedMilliseconds}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "Empty API response");
            }

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
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={content.Length}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, $"JSON error: {ex.Message}");
            }
            parseSw.Stop();

            totalSw.Stop();
            _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} http={response.Version} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={content.Length}");

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
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, $"Request timed out after {_timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("API request cancelled");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Cancelled, "Request cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"Network error: {ex.Message}");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error: {ex.Message}");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Unexpected, $"Unexpected error: {ex.Message}");
        }
    }
}

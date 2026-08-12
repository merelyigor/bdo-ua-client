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
            using var response = await _httpClient.GetAsync(url, linkedCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.Warning($"API error: {error}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Http, error);
            }

            var content = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.Warning("Empty API response");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "Empty API response");
            }

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
                _logger.Error($"JSON error: {ex.Message}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, $"JSON error: {ex.Message}");
            }

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

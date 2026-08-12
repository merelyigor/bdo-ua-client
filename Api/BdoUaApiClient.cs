using System.Net.Http.Json;
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

    public BdoUaApiClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ApiResult<ReleasesResponse>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/releases";
        _logger.Debug($"Fetching releases from {url}");

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.Warning($"API error: {error}");
                return ApiResult<ReleasesResponse>.Failure(error);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.Warning("Empty API response");
                return ApiResult<ReleasesResponse>.Failure("Empty API response");
            }

            var releases = JsonSerializer.Deserialize<ReleasesResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (releases == null)
            {
                _logger.Warning("Failed to deserialize API response");
                return ApiResult<ReleasesResponse>.Failure("Failed to deserialize API response");
            }

            _logger.Debug($"Successfully fetched releases: {releases.Data?.Modes?.Count ?? 0} modes");
            return ApiResult<ReleasesResponse>.Success(releases);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("API request cancelled");
            return ApiResult<ReleasesResponse>.Failure("Request cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"Network error: {ex.Message}");
            return ApiResult<ReleasesResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.Error($"JSON error: {ex.Message}");
            return ApiResult<ReleasesResponse>.Failure($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error: {ex.Message}");
            return ApiResult<ReleasesResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }
}

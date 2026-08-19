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
    private const int TimeoutSeconds = 15;

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

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
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
            _logger.Warning($"GitHub update: timeout after {TimeoutSeconds}s");
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
}

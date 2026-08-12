using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class ReleasesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("data")]
    public ReleaseData? Data { get; set; }
}

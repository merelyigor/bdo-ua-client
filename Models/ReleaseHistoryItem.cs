using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class ReleaseHistoryItem
{
    [JsonPropertyName("public_id")]
    public string? PublicId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("patch")]
    public int Patch { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("retired_at")]
    public string? RetiredAt { get; set; }
}

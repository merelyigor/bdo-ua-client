using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class LocalizationMode
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("public_name")]
    public string? PublicName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("audience")]
    public string? Audience { get; set; }

    [JsonPropertyName("current")]
    public CurrentRelease? Current { get; set; }

    [JsonPropertyName("history")]
    public List<ReleaseHistoryItem>? History { get; set; }
}

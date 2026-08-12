using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class GameTestInfo
{
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

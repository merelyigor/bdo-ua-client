using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class InstallPathPattern
{
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("launcher")]
    public string? Launcher { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

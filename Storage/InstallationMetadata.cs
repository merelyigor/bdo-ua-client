using System.Text.Json.Serialization;

namespace BdoClient.Storage;

public sealed class InstallationMetadata
{
    [JsonPropertyName("mode_slug")]
    public string? ModeSlug { get; set; }

    [JsonPropertyName("public_id")]
    public string? PublicId { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("game_patch")]
    public int GamePatch { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("installed_at")]
    public DateTimeOffset InstalledAt { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "api";
}

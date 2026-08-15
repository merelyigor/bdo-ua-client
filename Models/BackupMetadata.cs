using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class BackupMetadata
{
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("game_patch")]
    public int? GamePatch { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("installation_state")]
    public string? InstallationState { get; set; }
}

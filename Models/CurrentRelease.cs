using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class CurrentRelease
{
    [JsonPropertyName("public_id")]
    public string? PublicId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("patch")]
    public int Patch { get; set; }

    [JsonPropertyName("compatible_with_official_patch")]
    public bool CompatibleWithOfficialPatch { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("game_tested_at")]
    public string? GameTestedAt { get; set; }

    [JsonPropertyName("game_test")]
    public GameTestInfo? GameTest { get; set; }

    [JsonPropertyName("stats")]
    public StatsInfo? Stats { get; set; }

    [JsonPropertyName("announcements")]
    public AnnouncementsInfo? Announcements { get; set; }
}

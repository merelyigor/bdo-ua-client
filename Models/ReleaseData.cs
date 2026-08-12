using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class ReleaseData
{
    [JsonPropertyName("official_patch")]
    public int OfficialPatch { get; set; }

    [JsonPropertyName("official_patch_checked_at")]
    public string? OfficialPatchCheckedAt { get; set; }

    [JsonPropertyName("official_source_url")]
    public string? OfficialSourceUrl { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("install_path_patterns")]
    public List<InstallPathPattern>? InstallPathPatterns { get; set; }

    [JsonPropertyName("install_guide_url")]
    public string? InstallGuideUrl { get; set; }

    [JsonPropertyName("progress")]
    public ProgressInfo? Progress { get; set; }

    [JsonPropertyName("modes")]
    public List<LocalizationMode>? Modes { get; set; }
}

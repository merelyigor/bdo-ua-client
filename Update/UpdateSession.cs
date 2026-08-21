using System.Text.Json.Serialization;

namespace BdoClient.Update;

public sealed class UpdateSession
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("current_version")]
    public string CurrentVersion { get; set; } = "";

    [JsonPropertyName("target_version")]
    public string TargetVersion { get; set; } = "";

    [JsonPropertyName("target_tag")]
    public string TargetTag { get; set; } = "";

    [JsonPropertyName("target_path")]
    public string TargetPath { get; set; } = "";

    [JsonPropertyName("parent_pid")]
    public int ParentPid { get; set; }

    [JsonPropertyName("package_asset_name")]
    public string PackageAssetName { get; set; } = "";

    [JsonPropertyName("package_file_name")]
    public string? PackageFileName { get; set; }

    [JsonPropertyName("package_sha256")]
    public string PackageSha256 { get; set; } = "";

    [JsonPropertyName("staged_exe_sha256")]
    public string StagedExeSha256 { get; set; } = "";

    [JsonPropertyName("original_exe_sha256")]
    public string? OriginalExeSha256 { get; set; }

    public const string StateStaged = "staged";
    public const string StatePrepared = "prepared";
    public const string StateApplied = "applied";
}

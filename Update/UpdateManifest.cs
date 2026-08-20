using System.Text.Json.Serialization;

namespace BdoClient.Update;

public sealed class UpdateManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("commit_sha")]
    public string? CommitSha { get; set; }

    [JsonPropertyName("asset_name")]
    public string? AssetName { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("package_name")]
    public string? PackageName { get; set; }

    [JsonPropertyName("package_sha256")]
    public string? PackageSha256 { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("workflow_run_id")]
    public string? WorkflowRunId { get; set; }
}

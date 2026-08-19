namespace BdoClient.Update;

public sealed class UpdateSession
{
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string State { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string TargetVersion { get; set; } = "";
    public string TargetTag { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public int ParentPid { get; set; }
    public string PackageAssetName { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public string StagedExeSha256 { get; set; } = "";
}

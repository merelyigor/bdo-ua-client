namespace BdoClient.Models;

public sealed class RestorePointInfo
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int? GamePatch { get; set; }
    public string? Source { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool HasInstallationState { get; set; }
    public bool IsRestorable { get; set; }
}

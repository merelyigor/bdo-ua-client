using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class ProgressInfo
{
    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    [JsonPropertyName("translated_percent")]
    public double TranslatedPercent { get; set; }

    [JsonPropertyName("manual_rows")]
    public int ManualRows { get; set; }

    [JsonPropertyName("manual_percent")]
    public double ManualPercent { get; set; }

    [JsonPropertyName("machine_rows")]
    public int MachineRows { get; set; }

    [JsonPropertyName("machine_percent")]
    public double MachinePercent { get; set; }
}

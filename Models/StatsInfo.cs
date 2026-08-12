using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class StatsInfo
{
    [JsonPropertyName("rows_in_file")]
    public int RowsInFile { get; set; }
}

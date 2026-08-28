using System.Text.Json.Serialization;

namespace BdoClient.Storage;

public sealed class Config
{
    [JsonPropertyName("game_path")]
    public string? GamePath { get; set; }

    [JsonPropertyName("last_mode")]
    public string? LastMode { get; set; }

    [JsonPropertyName("autostart_prompt_dismissed")]
    public bool AutostartPromptDismissed { get; set; }
}

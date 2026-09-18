using System.Text.Json.Serialization;

namespace BdoClient.Storage;

public sealed class ApplicationConfig
{
    [JsonPropertyName("autostart_prompt_dismissed")]
    public bool AutostartPromptDismissed { get; set; }

    [JsonPropertyName("selected_game_id")]
    public string? SelectedGameId { get; set; }
}

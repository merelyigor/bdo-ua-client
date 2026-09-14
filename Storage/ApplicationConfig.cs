using System.Text.Json.Serialization;

namespace BdoClient.Storage;

public sealed class ApplicationConfig
{
    [JsonPropertyName("autostart_prompt_dismissed")]
    public bool AutostartPromptDismissed { get; set; }
}

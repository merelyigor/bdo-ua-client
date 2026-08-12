using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class AnnouncementsInfo
{
    [JsonPropertyName("discord_releases")]
    public AnnouncementChannel? DiscordReleases { get; set; }

    [JsonPropertyName("telegram_main")]
    public AnnouncementChannel? TelegramMain { get; set; }
}

public sealed class AnnouncementChannel
{
    [JsonPropertyName("sent")]
    public bool Sent { get; set; }

    [JsonPropertyName("sent_at")]
    public string? SentAt { get; set; }
}

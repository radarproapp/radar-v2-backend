namespace RadarV2.Models;

/// <summary>
/// Cache of a resolved YouTube channel id. YouTube's public Atom feed needs a channel_id; channels
/// are configured by handle (@name) and resolved once from the channel page, then remembered here.
/// </summary>
public class YouTubeChannelDoc
{
    public string ChannelId { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
}

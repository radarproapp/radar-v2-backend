namespace RadarV2.Models;

public class Clip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string? YourEdge { get; set; }
    public string? NextMove { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ContentItemId { get; set; }
    public string? MediaUrl { get; set; }
    public string MediaType { get; set; } = "video"; // "video" or "audio"
    public string? ThumbnailUrl { get; set; }
    public int DurationSeconds { get; set; } = 45;
    public bool IsSaved { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}

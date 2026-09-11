namespace RadarV2.Models;

public enum CaptureMode
{
    Link,
    Note,
    Voice,
    Photo
}

public class CapturedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public CaptureMode Mode { get; set; }
    public string Input { get; set; } = string.Empty;
    public string? Signal { get; set; }
    public string? WhyItMatters { get; set; }
    public string? AiSummary { get; set; }
    public string? Source { get; set; }
    public string? LinkedRoadmapId { get; set; }
    public bool IsProcessing { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

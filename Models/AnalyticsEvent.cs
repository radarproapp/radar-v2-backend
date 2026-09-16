namespace RadarV2.Models;

public enum AnalyticsEventType
{
    Impression,
    Open,
    Save,
    Unsave,
    NotRelevant,
    AddToRoadmap,
    CaptureCreated,
    ClipSaved,
    WhyRatedHelpful,
    WhyRatedNotHelpful,
}

/// <summary>
/// One user action or exposure, logged so ranking/"why" quality can be measured instead of
/// guessed at. See RADAR product-strategy notes: §3.1 "measure before you optimise".
/// </summary>
public class AnalyticsEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public AnalyticsEventType Type { get; set; }
    public string? ContentItemId { get; set; }
    public string? OpportunityId { get; set; }
    public string? ClipId { get; set; }

    /// <summary>Rank position in the list the item was shown at, when known (0-based).</summary>
    public int? Position { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

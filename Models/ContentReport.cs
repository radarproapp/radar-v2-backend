namespace RadarV2.Models;

public enum ReportReason
{
    Incorrect,
    LowQuality,
    BrokenLink,
    NotRelevantToLayer,
    Other
}

/// <summary>
/// A user-flagged problem with a content item, denormalised with the source name/tier at report
/// time so quality review (see product-strategy §"source quality needs continuous review") can
/// group by source without joining back through the ingestion registry. Manual review only for
/// now -- no automatic tier promotion/demotion yet.
/// </summary>
public class ContentReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string ItemSignal { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int CredibilityTier { get; set; }
    public ReportReason Reason { get; set; }
    public string? Note { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

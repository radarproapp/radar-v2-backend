using RadarV2.Models;

namespace RadarV2.Data.Documents;

public class OpportunityTrackingDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string OpportunityId { get; set; } = string.Empty;
    public OpportunityTrackingStatus Status { get; set; } = OpportunityTrackingStatus.None;
    public bool ReminderSet { get; set; }
    public string? FeedbackNotes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

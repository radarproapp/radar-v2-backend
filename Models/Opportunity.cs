namespace RadarV2.Models;

public class Opportunity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public OpportunityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Organisation { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? Deadline { get; set; }
    public List<string> Requirements { get; set; } = [];
    public int MatchScorePercent { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Location { get; set; }
    public bool IsRemote { get; set; }
    public bool IsSaved { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public int DaysUntilDeadline =>
        Deadline.HasValue ? (int)(Deadline.Value - DateTime.UtcNow).TotalDays : int.MaxValue;
}

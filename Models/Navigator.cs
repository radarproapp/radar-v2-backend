namespace RadarV2.Models;

public class NavigatorFocus
{
    public NavigatorCard Learn { get; set; } = new();
    public NavigatorCard Read { get; set; } = new();
    public NavigatorCard Watch { get; set; } = new();
    public NavigatorCard Listen { get; set; } = new();
    public NavigatorCard Apply { get; set; } = new();
    public NavigatorCard Build { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class NavigatorCard
{
    public NavigatorCardType CardType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string? EstimatedTime { get; set; }
    public string? AiSummary { get; set; }
    public string? ActionUrl { get; set; }
    public string? ContentItemId { get; set; }
    public string? OpportunityId { get; set; }

    // For Apply card
    public int? MatchScorePercent { get; set; }
    public int? DeadlineDays { get; set; }
}

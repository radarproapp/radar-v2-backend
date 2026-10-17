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
    public string? WhyItFits { get; set; }
    public double MatchConfidence { get; set; }
    public List<string> PreparationSteps { get; set; } = [];
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    // Opportunity Intelligence Fields
    public string RegionTag { get; set; } = "Global"; // Nigeria, Ghana, Africa, Remote, Global
    public string GoalTag { get; set; } = "Career"; // Career, Education, Business, Research, Networking, Funding, Skills
    public string Benefits { get; set; } = string.Empty; // e.g. Full tuition, $5,000 grant, $75k stipend
    public string Cost { get; set; } = "Free / Fully Funded";
    public List<string> WhoIsEligible { get; set; } = [];
    public List<string> RadarsAdvice { get; set; } = []; // Actionable tips on how to get ahead
    public List<string> NextMoveSteps { get; set; } = []; // Check eligibility → Prepare CV → Draft application → Apply
    public string SelectionProcess { get; set; } = string.Empty;
    public string Status { get; set; } = "Open"; // Open, ClosingSoon, Closed
    public OpportunityTrackingStatus UserTrackingStatus { get; set; } = OpportunityTrackingStatus.None;
    public bool ReminderSet { get; set; }

    public int DaysUntilDeadline =>
        Deadline.HasValue ? (int)Math.Max(0, (Deadline.Value - DateTime.UtcNow).TotalDays) : int.MaxValue;

    public string MatchTier =>
        MatchScorePercent >= 80 ? "StrongMatch" :
        MatchScorePercent >= 60 ? "Relevant" : "Explore";

    public string UrgencyLabel =>
        DaysUntilDeadline <= 7 ? "🔴 Urgent" :
        DaysUntilDeadline <= 21 ? "🟡 2 weeks left" : "🟢 Recently opened";
}

public class DailyOpportunityFeed
{
    public int TotalCount { get; set; } = 12;
    public string Message { get; set; } = "12 new opportunities for you today";
    public string PrimaryFocus { get; set; } = "Cybersecurity";
    public List<Opportunity> Items { get; set; } = [];
    public Dictionary<string, int> TierCounts { get; set; } = [];
    public Dictionary<string, int> UrgencyCounts { get; set; } = [];
}

public class OpportunityStatusUpdateRequest
{
    public OpportunityTrackingStatus Status { get; set; }
    public string? Notes { get; set; }
}

public class OpportunityReminderRequest
{
    public bool ReminderSet { get; set; }
}

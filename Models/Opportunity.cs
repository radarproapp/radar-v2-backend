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

    /// <summary>True when this user has not been shown this opportunity in a previous daily batch.</summary>
    public bool IsNewToYou { get; set; } = true;
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

    /// <summary>
    /// Days remaining, floored at zero, or <see cref="int.MaxValue"/> when no deadline is known.
    ///
    /// The floor is why <see cref="HasClosed"/> exists: a deadline that passed yesterday arrives
    /// here as 0, which is indistinguishable from "due today". Callers deciding what to show must
    /// ask whether it has closed first.
    /// </summary>
    public int DaysUntilDeadline =>
        Deadline.HasValue ? (int)Math.Max(0, (Deadline.Value - DateTime.UtcNow).TotalDays) : int.MaxValue;

    /// <summary>Whether the deadline has already passed.</summary>
    public bool HasClosed => Deadline.HasValue && Deadline.Value < DateTime.UtcNow;

    public string MatchTier =>
        MatchScorePercent >= 80 ? "StrongMatch" :
        MatchScorePercent >= 60 ? "Relevant" : "Explore";

    /// <summary>
    /// What to say about the deadline.
    ///
    /// Two of these used to be false. An opportunity with no deadline fell through to "Recently
    /// opened" — nothing here checks when anything opened, so that was invented. And a *closed*
    /// one was labelled "Urgent", because DaysUntilDeadline floors at zero, so a user could be
    /// hurried into applying for something that shut last week.
    /// </summary>
    public string UrgencyLabel =>
        HasClosed ? "⚫ Closed" :
        !Deadline.HasValue ? "🟢 No deadline given" :
        DaysUntilDeadline <= 7 ? "🔴 Urgent" :
        DaysUntilDeadline <= 21 ? "🟡 2 weeks left" : "🟢 Open";
}

public class DailyOpportunityFeed
{
    public int TotalCount { get; set; } = 12;
    public string Message { get; set; } = "12 new opportunities for you today";
    /// <summary>
    /// The field this batch was matched against. Empty when the user has not told Radar what they
    /// are working on — it used to default to "Cybersecurity", so a brand new user was shown a
    /// sentence claiming Radar had matched against a focus they never chose.
    /// </summary>
    public string PrimaryFocus { get; set; } = string.Empty;
    public List<Opportunity> Items { get; set; } = [];
    public Dictionary<string, int> TierCounts { get; set; } = [];
    public Dictionary<string, int> UrgencyCounts { get; set; } = [];

    /// <summary>How many of today's batch this user has genuinely not seen before.</summary>
    public int NewToYouCount { get; set; }

    /// <summary>
    /// Opportunities this user has already been shown on an earlier day and still has not acted on.
    /// Kept out of the daily batch but still reachable, so a deadline cannot quietly pass just
    /// because the item stopped being new.
    /// </summary>
    public List<Opportunity> SeenEarlier { get; set; } = [];
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

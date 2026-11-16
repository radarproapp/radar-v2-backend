using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

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
    /// <summary>Who may apply, in the source's own words.</summary>
    /// <remarks>
    /// Kept for display. <see cref="Eligibility"/> beside it is the same question in a form
    /// something can compare against a person - prose can be shown and cannot be matched.
    /// </remarks>
    public List<string> WhoIsEligible { get; set; } = [];

    /// <summary>Who may apply, in a form the matcher can read.</summary>
    /// <remarks>
    /// Empty on everything ingested before this existed, and empty means "not stated" rather
    /// than "open to all" - see Eligibility.WasStated. A screen that claims to check eligibility
    /// must not treat silence as permission.
    /// </remarks>
    public Eligibility Eligibility { get; set; } = new();

    /// <summary>Which of the eight families this belongs to.</summary>
    /// <remarks>
    /// Derived from the type rather than stored, so the two can never disagree and nothing has
    /// to be backfilled.
    /// </remarks>
    [BsonIgnore]
    public OpportunityFamily Family => OpportunityFamilies.Of(Type);

    /// <summary>
    /// What the eligibility check concluded for the person being served.
    /// </summary>
    /// <remarks>
    /// Computed per request and never stored, like the match score beside it: the answer is about
    /// a particular reader, and persisting it would be writing one person's circumstances onto a
    /// posting everybody sees.
    ///
    /// Carried on the record rather than returned separately so the card that shows an
    /// opportunity can show why it is there. An eligibility check nobody can read the result of
    /// is a check that may as well not have run.
    /// </remarks>
    /// <remarks>
    /// Null until the check has run, and deliberately not defaulted to "Unchecked". Those are
    /// different statements: Unchecked means the posting records no criteria, and null means
    /// nobody has compared this reader against it yet. The surfaces that list saved and tracked
    /// opportunities do not gate - hiding something somebody deliberately kept would be wrong -
    /// so they would have reported "nothing stated" about postings that state plenty.
    /// </remarks>
    [BsonIgnore]
    public string? EligibilityVerdict { get; set; }

    /// <summary>Why, in sentences a person can read.</summary>
    /// <remarks>
    /// Includes what could not be checked, not only what passed or failed. "Open to Nigerian
    /// nationals, Radar has not asked yours" is the line that makes a partial check honest, and
    /// leaving it out is how a partial check comes to look like a complete one.
    /// </remarks>
    [BsonIgnore]
    public List<string> EligibilityNotes { get; set; } = [];

    /// <summary>What Radar is permitted to show from the source this came from.</summary>
    /// <remarks>
    /// Carried onto the record so a reader of one opportunity can answer the question without
    /// looking up where it came from - and so a listing whose rights were never confirmed cannot
    /// be rendered as though they had been.
    ///
    /// Stored and never sent. JsonIgnore does not touch the BSON serialiser, so this persists
    /// exactly as before and stops appearing in API responses - where it was going out on every
    /// opportunity, because a caller that omits ?fields= gets the whole object. Which sources
    /// Radar considers unchecked is a question about Radar's legal position, not information a
    /// reader of a job posting is owed, and it reads as an admission in a payload anybody can
    /// fetch.
    /// </remarks>
    [JsonIgnore]
    public Redistribution Rights { get; set; } = Redistribution.Unconfirmed;

    /// <summary>When a person or a check last confirmed this is still open.</summary>
    /// <remarks>
    /// The brief asks only for opportunities that are currently open. A deadline in the future
    /// is not the same as a posting that still exists, and without this there is no way to tell
    /// a verified listing from one nobody has looked at since it was fetched.
    /// </remarks>
    [BsonIgnoreIfNull]
    public DateTime? LastVerifiedAt { get; set; }
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
        UrgencyLevel switch
        {
            OpportunityUrgency.Closed => "Closed",
            OpportunityUrgency.NoDeadline => "No deadline given",
            OpportunityUrgency.Urgent => "Urgent",
            OpportunityUrgency.Soon => "2 weeks left",
            _ => "Open",
        };

    /// <summary>
    /// How pressing the deadline is, as a value rather than a picture.
    ///
    /// The label used to carry a coloured circle emoji before the word - "Urgent" with a red dot
    /// in front of it. That put a presentation decision
    /// inside a data field: the server chose a glyph that renders differently on every platform,
    /// cannot be recoloured for a theme, and is read aloud as "large red circle" before the word
    /// that matters. The client draws the state; the server says what the state is.
    /// </summary>
    public OpportunityUrgency UrgencyLevel =>
        HasClosed ? OpportunityUrgency.Closed :
        !Deadline.HasValue ? OpportunityUrgency.NoDeadline :
        DaysUntilDeadline <= 7 ? OpportunityUrgency.Urgent :
        DaysUntilDeadline <= 21 ? OpportunityUrgency.Soon : OpportunityUrgency.Open;
}

/// <summary>How pressing an opportunity's deadline is.</summary>
public enum OpportunityUrgency
{
    Open,
    Soon,
    Urgent,
    Closed,
    NoDeadline,
}

public class DailyOpportunityFeed
{
    /// <summary>
    /// Set when the learner has not started anywhere yet, so there is nothing to match against.
    /// </summary>
    public string? EmptyReason { get; set; }

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

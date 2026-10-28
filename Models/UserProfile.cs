using MongoDB.Bson.Serialization.Attributes;
namespace RadarV2.Models;

public class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The account this profile belongs to.
    /// </summary>
    /// <remarks>
    /// Added so one account can hold several profiles — a student who graduates keeps their
    /// student path and starts a graduate one beside it, rather than overwriting themselves.
    ///
    /// The rest of Radar already works this way without knowing it: roadmaps, saved items,
    /// captures, notes and behavioural signals are all keyed by the profile id, so each profile
    /// already has its own feed, its own path and its own history. What was missing was any way
    /// to have a second one. Backfilled on read for profiles written before this existed.
    /// </remarks>
    public string UserId { get; set; } = string.Empty;

    /// <summary>What the learner calls this profile. Defaults to the persona.</summary>
    public string Label { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public PersonaType Persona { get; set; }
    public string PrimaryGoal { get; set; } = string.Empty;
    public List<string> Interests { get; set; } = [];
    public string Region { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;

    // Layer 1 of the personalization model: the situation that makes an item relevant beyond the
    // raw interests. All optional, captured progressively (onboarding, Edit Profile, follow-ups).
    public List<string> Problems { get; set; } = [];
    public string CurrentIntent { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public string TargetIndustry { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = [];
    /// <summary>
    /// What the learner wants to achieve — box 2 of personalisation.
    /// </summary>
    /// <remarks>
    /// A list, because the box allows several and people have several: find a job, learn a
    /// skill, win a scholarship. <see cref="PrimaryGoal"/> is a single string and stays for the
    /// learning path, which is built around one goal at a time.
    /// </remarks>
    public List<string> Goals { get; set; } = [];

    public List<string> OpportunityPreferences { get; set; } = [];
    public List<string> Geography { get; set; } = [];
    public List<string> DecisionNeeds { get; set; } = [];

    public Dictionary<string, string> PersonaDetails { get; set; } = [];
    public List<UserInterestContext> InterestContexts { get; set; } = [];
    public List<string> DominantInterests { get; set; } = [];
    public List<InterestPath> InterestPaths { get; set; } = [];
    public Dictionary<string, int> InterestActivityScores { get; set; } = [];
    public NotificationPrefs Notifications { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool OnboardingComplete { get; set; }

    /// <summary>
    /// Whether the learner has told Radar enough for a feed to be theirs.
    /// </summary>
    /// <remarks>
    /// Derived, not stored. A flag would need a migration and would lie about anyone who
    /// completed the older onboarding, which never asked for goals — they would read as
    /// personalised while box 2 was empty. Deriving it treats them correctly: not yet, ask once.
    ///
    /// Opportunity preferences are asked for but not required. Someone who wants nothing applied
    /// for should still get a feed.
    /// </remarks>
    [BsonIgnore]
    public bool IsPersonalised =>
        Interests.Any(i => !string.IsNullOrWhiteSpace(i))
        && Goals.Any(g => !string.IsNullOrWhiteSpace(g));
    public UserStats Stats { get; set; } = new();
}

/// <summary>
/// The user's intent for one interest. The same interest can therefore produce
/// different content, opportunities and actions for different people.
/// </summary>
public class UserInterestContext
{
    public string Interest { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Level { get; set; } = "Beginner";
    public string Lens { get; set; } = string.Empty;
}

public class InterestPath
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public List<string> Interests { get; set; } = [];
    public int RelationshipStrength { get; set; }
    public bool IsPrimary { get; set; }
    public string Priority { get; set; } = "Explore occasionally";
}

public class NotificationPrefs
{
    public bool WeeklyBrief { get; set; } = true;
    public bool OpportunityDeadlines { get; set; } = true;
    public bool RoadmapReminders { get; set; }

    /// <summary>
    /// A short message each day covering what is new in the topics they follow.
    /// </summary>
    /// <remarks>
    /// Off by default, unlike the weekly one, and the difference is deliberate. Daily is the most
    /// aggressive cadence a product can choose; opting somebody into it on their behalf is how an
    /// entire sending domain ends up in spam folders, which costs the password reset too.
    ///
    /// Nothing is sent on a day with nothing to say, so turning it on is not a commitment to a
    /// message every morning.
    /// </remarks>
    public bool DailyDigest { get; set; }
}

public class UserStats
{
    public int LearningStreakDays { get; set; }
    public int SavedResourcesCount { get; set; }
    public int RoadmapProgressPercent { get; set; }
    public int OpportunitiesApplied { get; set; }
    public int ProjectsCompleted { get; set; }
    public int LessonsCompleted { get; set; }
    public int ArticlesRead { get; set; }
    public int PodcastsFinished { get; set; }
    public int VideosWatched { get; set; }
    public int ResearchPapersRead { get; set; }
    public DateTime? LastActivityDate { get; set; }
}

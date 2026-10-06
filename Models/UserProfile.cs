namespace RadarV2.Models;

public class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
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

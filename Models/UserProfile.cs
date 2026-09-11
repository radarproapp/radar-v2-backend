namespace RadarV2.Models;

public class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public PersonaType Persona { get; set; }
    public string PrimaryGoal { get; set; } = string.Empty;
    public List<string> Interests { get; set; } = [];
    public string Region { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public Dictionary<string, string> PersonaDetails { get; set; } = [];
    public NotificationPrefs Notifications { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool OnboardingComplete { get; set; }
    public UserStats Stats { get; set; } = new();
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

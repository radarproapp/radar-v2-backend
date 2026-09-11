namespace RadarV2.Models;

public class SourceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string TrustNote { get; set; } = string.Empty;
    public string? AiEdge { get; set; }
    public int ItemsInRadar { get; set; }
    public int ItemsRead { get; set; }
    public string PublishFrequency { get; set; } = string.Empty;
    public bool IsFollowing { get; set; }
    public bool IncludeInWeeklyBrief { get; set; } = true;
    public bool PrioritiseInFeed { get; set; }
    public List<ContentItem> RecentItems { get; set; } = [];
}

public class TopicProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SummarySource { get; set; } = string.Empty;
    public DateTime SummaryUpdatedAt { get; set; } = DateTime.UtcNow;
    public string? AiEdge { get; set; }
    public int ItemsInRadar { get; set; }
    public int ItemsThisWeek { get; set; }
    public int SourceCount { get; set; }
    public bool IsFollowing { get; set; }
    public bool OnRoadmap { get; set; }
    public string? RoadmapModuleName { get; set; }
    public List<string> RoadmapContents { get; set; } = [];
    public List<TopicAlert> Alerts { get; set; } = [];
    public List<ContentItem> RecentItems { get; set; } = [];
}

public class TopicAlert
{
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

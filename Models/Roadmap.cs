namespace RadarV2.Models;

public class GrowthRoadmap
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public List<RoadmapModule> Modules { get; set; } = [];
    public int ProgressPercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }
}

public class RoadmapModule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsLocked { get; set; }
    public List<RoadmapLesson> Lessons { get; set; } = [];
    public List<string> ContentItemIds { get; set; } = [];
}

public class RoadmapLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsCompleted { get; set; }
    public string? EstimatedTime { get; set; }
}

namespace RadarV2.Models;

public class GrowthRoadmap
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Interest { get; set; } = string.Empty;
    public string Level { get; set; } = "Beginner";
    public List<RoadmapModule> Modules { get; set; } = [];
    public List<LearningResource> Resources { get; set; } = [];
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
    public string Level { get; set; } = "Beginner";
    public bool IsCompleted { get; set; }
    public bool IsLocked { get; set; }
    public List<RoadmapLesson> Lessons { get; set; } = [];
    public List<string> ContentItemIds { get; set; } = [];
    public List<LearningResource> Resources { get; set; } = [];
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

public enum LearningResourceType
{
    TutorialVideo = 0,       // 80% How-to / Tutorial video
    NewDevelopmentVideo = 1, // 20% Latest developments video
    BlogPost = 2,            // 20% Blog post / essay on new developments
}

public class LearningResource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? EmbedUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Source { get; set; } = "YouTube";
    public string? ChannelOrAuthor { get; set; }
    public string? Duration { get; set; }
    public string Level { get; set; } = "Beginner"; // "Beginner", "Intermediate", "Advanced"
    public LearningResourceType Type { get; set; } = LearningResourceType.TutorialVideo;
    public bool IsHowToTutorial => Type == LearningResourceType.TutorialVideo;

    // Detailed How-To Tutorial format:
    public string? WhatYouWillBuild { get; set; }
    public List<TutorialStep> StepByStepBreakdown { get; set; } = [];

    // Detailed 4-part intelligence format:
    public string WhatHappened { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string YourEdge { get; set; } = string.Empty;
    public string NextMove { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }
}

public class TutorialStep
{
    public int StepNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public string? CodeSnippetOrKeyPoint { get; set; }
}

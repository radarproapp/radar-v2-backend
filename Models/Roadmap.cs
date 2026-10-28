using MongoDB.Bson.Serialization.Attributes;
namespace RadarV2.Models;

public class GrowthRoadmap
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Interest { get; set; } = string.Empty;
    public string Level { get; set; } = "Beginner";
    public string CurrentStage { get; set; } = "Beginner"; // "Beginner", "Intermediate", "Advanced", "Application"

    /// <summary>
    /// Which template produced these modules. Without it there is no way to tell a roadmap built
    /// by an older, worse template from a current one — so a user who had "Foundations of Get
    /// Promotion" generated before the goal interpreter existed kept it forever, because the read
    /// path only ever re-tailored stages and never rebuilt modules.
    /// </summary>
    public string? TemplateKey { get; set; }
    public List<RoadmapModule> Modules { get; set; } = [];
    public List<LearningResource> Resources { get; set; } = [];
    public int ProgressPercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }

    // Mastery & Stage Tracking
    public Dictionary<string, int> StageMastery { get; set; } = new()
    {
        ["Beginner"] = 0,
        ["Intermediate"] = 0,
        ["Advanced"] = 0,
        ["Application"] = 0
    };
    public List<string> CompletedResourceIds { get; set; } = [];
    public List<string> CompletedExerciseIds { get; set; } = [];
    public List<QuizPerformanceRecord> QuizResults { get; set; } = [];
    public List<string> WeakAreas { get; set; } = [];
    public string? AdaptiveRecommendation { get; set; }
}

public class RoadmapModule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StageGoal { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Level { get; set; } = "Beginner"; // "Beginner", "Intermediate", "Advanced", "Application"
    public bool IsCompleted { get; set; }
    public bool IsLocked { get; set; }
    public int MasteryPercent { get; set; }
    public List<RoadmapLesson> Lessons { get; set; } = [];

    /// <summary>
    /// The thing you build while learning this stage, not after it. Radar Learn is about building
    /// skills and showing capability at the same time, so every stage carries a project rather than
    /// leaving "make a portfolio" to the final stage.
    /// </summary>
    public StageProject? Project { get; set; }
    public List<string> ContentItemIds { get; set; } = [];
    public List<LearningResource> Resources { get; set; } = [];
}

/// <summary>
/// A small, concrete build that runs alongside a stage's lessons. It exists so a learner finishes
/// each stage holding evidence, not just having watched something.
/// </summary>
public class StageProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;

    /// <summary>What they will have at the end, in plain words.</summary>
    public string Deliverable { get; set; } = string.Empty;

    /// <summary>Why this proves something an employer or client cares about.</summary>
    public string WhyItProvesCapability { get; set; } = string.Empty;

    public string? EstimatedTime { get; set; }
    public List<string> Steps { get; set; } = [];
    public bool IsCompleted { get; set; }
}

public class RoadmapLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsCompleted { get; set; }
    public string? EstimatedTime { get; set; }

    /// <summary>
    /// A short phrase to search for this lesson, when the human-readable title would make a poor
    /// query. "What hospital nurse manager actually involves day to day" reads well on a card and
    /// finds nothing on YouTube; "hospital nurse management for beginners" finds the lesson.
    /// </summary>
    public string? SearchHint { get; set; }

    /// <summary>
    /// Why this lesson has no resources, when it has none.
    /// </summary>
    /// <remarks>
    /// Section 19 asks for a clear message when there are no results, and there are two different
    /// nothings here that a learner must be able to tell apart: Radar looked and found nothing
    /// about this lesson, or Radar could not look. The first is a fact about the corpus and the
    /// second is a fault, and showing the same blank space for both hides the fault.
    ///
    /// Empty when the lesson has resources.
    /// </remarks>
    public string ResourceNote { get; set; } = string.Empty;

    /// <summary>
    /// Five videos and two articles for this lesson, each summarised.
    /// </summary>
    /// <remarks>
    /// Per lesson rather than per stage. Resources used to attach to the module, and when none
    /// matched the module's level the code fell back to handing it every resource on the path —
    /// so a lesson on ARP spoofing could show the same list as a lesson on VLAN hopping.
    ///
    /// Fewer than seven is normal and not padded. A lesson with three real videos and no article
    /// shows three videos; filling the gap with a search link would be a resource in name only,
    /// which is the same reason podcasts are absent from LearningResourceType.
    /// </remarks>
    public List<LessonResource> Resources { get; set; } = [];

    /// <summary>
    /// Videos for this lesson, newest search first.
    /// </summary>
    [BsonIgnore]
    public IEnumerable<LessonResource> Videos =>
        Resources.Where(r => r.Kind == LessonResourceKind.Video);

    /// <summary>Articles and blog posts for this lesson.</summary>
    [BsonIgnore]
    public IEnumerable<LessonResource> Articles =>
        Resources.Where(r => r.Kind == LessonResourceKind.Article);
}

public enum LearningResourceType
{
    TutorialVideo = 0,       // 80% How-to / Tutorial video
    NewDevelopmentVideo = 1, // 20% Latest developments video
    BlogPost = 2,            // 20% Blog post / essay on new developments

    /// <summary>
    /// Something the learner builds rather than watches.
    ///
    /// Spec 6.2 asks for a mix across tutorials, podcasts, blogs, papers, docs and projects. This
    /// is the one Radar can supply honestly today: StageProjectCatalog already produces a real,
    /// specific project per stage and subject. The others need sources that do not exist yet, and
    /// a search link dressed up as a podcast would be a format in name only.
    /// </summary>
    Project = 3,
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
    public string? EstimatedTime { get; set; } // e.g. "45 minutes"
    public int Order { get; set; } = 1;        // Sequence in stage (1, 2, 3...)
    public string SequenceRole { get; set; } = "Start here"; // "Start here", "Next", "Practice", "Capstone", "New Development"
    public string Level { get; set; } = "Beginner"; // "Beginner", "Intermediate", "Advanced", "Application"
    public LearningResourceType Type { get; set; } = LearningResourceType.TutorialVideo;
    public bool IsHowToTutorial => Type == LearningResourceType.TutorialVideo;
    public bool IsNewDevelopment => Type == LearningResourceType.NewDevelopmentVideo || Type == LearningResourceType.BlogPost;

    // Why am I learning this? (Connection to user's goal)
    public string WhyAmILearningThis { get; set; } = string.Empty;

    // Detailed How-To Tutorial format:
    public string? WhatYouWillBuild { get; set; }
    public List<TutorialStep> StepByStepBreakdown { get; set; } = [];

    // Practice Task
    public string? PracticeExerciseTitle { get; set; }
    public string? PracticeExercise { get; set; }
    public bool IsExerciseCompleted { get; set; }
    public string? QuizTopic { get; set; }
    public List<string> KeyConcepts { get; set; } = [];

    // Detailed 4-part intelligence format (for news / developments / blogs):
    public string WhatHappened { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
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

public class QuizPerformanceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Topic { get; set; } = string.Empty;
    public string Stage { get; set; } = "Beginner";
    public int ScorePercent { get; set; }
    public bool Passed => ScorePercent >= 75;
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;
}

namespace RadarV2.Models;

public enum HubResourceCategory
{
    Learn = 0,        // Tutorials, courses, how-to videos, technical guides, documentation
    Understand = 1,   // Articles, blog posts, explainers, podcasts, interviews
    Research = 2,     // Academic papers, conference papers, research reports, benchmarks
    Apply = 3,        // Projects, case studies, practical exercises, challenges
    StayCurrent = 4   // New technologies, developments, announcements, emerging practices
}

public enum HubMediaFormat
{
    Video = 0,
    AudioPodcast = 1,
    Article = 2,
    AcademicPaper = 3,
    Documentation = 4,
    ProjectGuide = 5
}

public class AcademicPaperBreakdown
{
    public string Introduction { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public string AimAndObjectives { get; set; } = string.Empty;
    public string Methodology { get; set; } = string.Empty;
    public string KeyArguments { get; set; } = string.Empty;
    public string Findings { get; set; } = string.Empty;
    public string Contribution { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;

    /// <summary>
    /// Where this paper sits in time: Recent, Established, or Foundational.
    ///
    /// Spec 8.4. A 1998 paper can be the foundational text of its field and still be the wrong
    /// thing to copy as current practice. Without this the reader has to notice the year and draw
    /// the conclusion themselves, which is exactly what nobody does while skimming.
    /// </summary>
    public string Era { get; set; } = string.Empty;

    /// <summary>
    /// What the authors say their work cannot do.
    ///
    /// The most useful section of any paper and the one most often dropped. A reader deciding
    /// whether to act on a finding needs to know the sample was 40 undergraduates before they
    /// generalise it to a company. Extracted, never inferred - a paper that states no limitation
    /// gets <see cref="AcademicPaperSynthesizer.NotStated"/> rather than a polite one invented on
    /// its behalf.
    /// </summary>
    public string Limitations { get; set; } = string.Empty;

    /// <summary>What the authors say should be studied next, where they say it.</summary>
    public string FutureWork { get; set; } = string.Empty;

    public int TotalWordCount =>
        (Introduction + " " + Problem + " " + AimAndObjectives + " " + Methodology + " " + KeyArguments + " " + Findings + " " + Contribution)
        .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}

public class PodcastBreakdown
{
    public List<string> KeyIdeas { get; set; } = [];
    public List<string> ImportantPoints { get; set; } = [];
    public string WhyItMatters { get; set; } = string.Empty;
    public List<string> Takeaways { get; set; } = [];
    public string NextMove { get; set; } = string.Empty;
}

public class HubResourceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string PrimarySkill { get; set; } = string.Empty;
    public string SubInterest { get; set; } = string.Empty; // e.g. "Network Security", "Ethical Hacking", "Cloud Security"
    public string Level { get; set; } = "Beginner"; // "Beginner", "Intermediate", "Advanced", "Application"
    public HubResourceCategory Category { get; set; } = HubResourceCategory.Learn;
    public HubMediaFormat Format { get; set; } = HubMediaFormat.Video;
    public string Url { get; set; } = string.Empty;
    public string? MediaUrl { get; set; } // YouTube embed or audio stream URL
    public string? ThumbnailUrl { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? AuthorOrChannel { get; set; }
    public string? DurationOrReadTime { get; set; }
    public int RankingScore { get; set; } = 95;

    // "Why this resource?" Personalized reason
    public string WhyThisResource { get; set; } = string.Empty;

    // Tutorial Breakdown (Learn / Apply)
    public string? WhatYouWillLearn { get; set; }
    public List<TutorialStep> StepByStepBreakdown { get; set; } = [];
    public string? PracticeExerciseTitle { get; set; }
    public string? PracticeExercise { get; set; }

    // Standard 4-part intelligence (Understand / Stay Current)
    public string WhatHappened { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string NextMove { get; set; } = string.Empty;

    // Academic Paper 8-part Breakdown (Research)
    public AcademicPaperBreakdown? PaperBreakdown { get; set; }

    // Podcast Breakdown (Understand)
    public PodcastBreakdown? PodcastBreakdown { get; set; }

    // Associated Knowledge Graph Nodes
    public List<string> ConnectedNodeIds { get; set; } = [];

    public bool IsCompleted { get; set; }
    public bool IsSaved { get; set; }

    /// <summary>
    /// Whether the hands-on exercise attached to this resource has been done.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IsCompleted"/>: reading a tutorial and doing its lab are
    /// different acts, and the lab is the one that produces something to show.
    ///
    /// This did not exist. Completion was communicated by appending the literal text
    /// " [Completed]" to <see cref="PracticeExercise"/>, so the only way a client could detect it
    /// was to search the instructions for a word — and the instructions were corrupted in the
    /// process. The whole chain behind it worked; there was no flag to render and so no button to
    /// press, which is why the storage had been there, unreachable, since it was written.
    /// </remarks>
    public bool IsExerciseCompleted { get; set; }
}

public class KnowledgeGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Category { get; set; } = "Topic"; // "Goal", "Skill", "SubInterest", "Topic", "Concept", "Project", "Opportunity"
    public string Status { get; set; } = "Planned";  // "Mastered", "Learning", "WeakArea", "NextUp", "Planned"
    public int ConfidencePercent { get; set; } = 50;
    public string Summary { get; set; } = string.Empty;
    public string? LinkedSubInterest { get; set; }
    public string? Stage { get; set; }
    public List<string> ConnectedNodeIds { get; set; } = [];
}

public class KnowledgeGraphEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Relationship { get; set; } = "leads_to"; // "requires", "subskill_of", "applies_to", "unlocks"
}

public class NextBestAction
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string WhyNowReason { get; set; } = string.Empty;
    public string EstimatedTime { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Stage { get; set; } = "Beginner";
    public string SubInterest { get; set; } = string.Empty;
}

public class LearnHubPayload
{
    /// <summary>
    /// Set when there is nothing to show because the learner has not built a path yet.
    /// </summary>
    /// <remarks>
    /// The Hub used to fall back to the learner's stated interests, or their degree, and present
    /// a curriculum for whatever that implied — the same guessing that was removed from My path.
    /// A learner who has answered nothing gets an empty Hub and an invitation, not a hub for a
    /// subject nobody picked.
    /// </remarks>
    public string? EmptyReason { get; set; }

    public string PrimarySkill { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = "Beginner";
    public List<string> SubInterests { get; set; } = [];
    public string ActiveSubInterest { get; set; } = string.Empty;
    public NextBestAction NextAction { get; set; } = new();
    public List<KnowledgeGraphNode> GraphNodes { get; set; } = [];
    public List<KnowledgeGraphEdge> GraphEdges { get; set; } = [];
    public List<HubResourceItem> Resources { get; set; } = [];
    public int TotalMasteredNodes { get; set; }
    public int TotalLearningNodes { get; set; }
    public int TotalWeakNodes { get; set; }
}

public class NodeActivityRequest
{
    public string NodeId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = "Completed"; // "Completed", "QuizPassed", "QuizFailed", "Practiced"
    public int ScorePercent { get; set; } = 100;
}

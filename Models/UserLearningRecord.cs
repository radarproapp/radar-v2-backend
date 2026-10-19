namespace RadarV2.Models;

/// <summary>
/// One consolidated record of what Radar knows about a user's learning.
///
/// The pieces already existed but were scattered: declared answers on <see cref="UserProfile"/>,
/// observed behaviour in <c>BehavioralSignal</c>, mastery and quiz history on the roadmap. Nothing
/// composed them, so roadmap generation still read a single goal string — which is why two people
/// with the same goal and wildly different experience got an identical path starting at lesson one.
///
/// This record is the input to tailoring. It is rebuilt from its sources rather than being a
/// separate source of truth, so it can never drift from the data it summarises.
/// </summary>
public class UserLearningRecord
{
    public string UserId { get; set; } = string.Empty;

    // ── What the user told us ────────────────────────────────────────────────
    public string PrimaryGoal { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public string TargetIndustry { get; set; } = string.Empty;
    public List<string> Interests { get; set; } = [];
    public List<string> Capabilities { get; set; } = [];
    public List<string> Problems { get; set; } = [];
    public string Location { get; set; } = string.Empty;

    /// <summary>Level the user stated at onboarding, before any evidence.</summary>
    public string DeclaredLevel { get; set; } = "Beginner";

    // ── What Radar observed ──────────────────────────────────────────────────
    /// <summary>Terms the user keeps engaging with, strongest first.</summary>
    public List<RecordSignal> Signals { get; set; } = [];

    /// <summary>Per-topic evidence drawn from quizzes and completions.</summary>
    public List<TopicEvidence> Topics { get; set; } = [];

    public List<string> StrongAreas { get; set; } = [];
    public List<string> WeakAreas { get; set; } = [];

    public int LessonsCompleted { get; set; }
    public int ResourcesCompleted { get; set; }
    public int QuizzesTaken { get; set; }
    public int AverageQuizScore { get; set; }

    // ── What Radar concluded ─────────────────────────────────────────────────
    /// <summary>
    /// Stage the roadmap should actually start at. Evidence can move someone past Beginner; with
    /// no evidence this stays at what they declared.
    /// </summary>
    public string RecommendedStartStage { get; set; } = "Beginner";

    /// <summary>Topics to pull forward — weak areas first, then strong recent interests.</summary>
    public List<string> PriorityTopics { get; set; } = [];

    /// <summary>Topics the user has already demonstrated, safe to mark done.</summary>
    public List<string> MasteredTopics { get; set; } = [];

    /// <summary>
    /// 0..1 — how much Radar actually knows about this person. Low confidence means tailoring
    /// should stay close to what they declared instead of inventing a bespoke path from noise.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>Plain-English summary of the record, shown to the user so personalisation is visible.</summary>
    public string Summary { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RecordSignal
{
    public string Term { get; set; } = string.Empty;
    public double Strength { get; set; }
    public int TimesEngaged { get; set; }

    /// <summary>"Declared" when they told us, "Inferred" when Radar worked it out from behaviour.</summary>
    public string Source { get; set; } = "Inferred";
}

public class TopicEvidence
{
    public string Topic { get; set; } = string.Empty;
    public int MasteryPercent { get; set; }
    public int Attempts { get; set; }

    /// <summary>"Mastered", "Learning", "Struggling" or "NotStarted".</summary>
    public string Status { get; set; } = "NotStarted";

    public DateTime? LastSeenAt { get; set; }
}

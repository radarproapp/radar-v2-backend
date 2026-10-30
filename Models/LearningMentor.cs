namespace RadarV2.Models;

public enum MentorMode
{
    Chat,
    Practice,
    Plan,
    Review,
    Mastery,
    Challenge
}

public class SubtopicMasteryItem
{
    public string Name { get; set; } = string.Empty;
    public int MasteryPercent { get; set; } = 50;
    public string Status { get; set; } = "In Progress"; // "Mastered", "Strong", "WeakArea", "In Progress"
    public int TotalQuestions { get; set; }
    public int CorrectAnswers { get; set; }
}

public class TopicMastery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty; // e.g. "SQL", "Cybersecurity", "Data Analytics"
    public int MasteryPercent { get; set; } = 50;
    public List<SubtopicMasteryItem> Subtopics { get; set; } = [];
    public List<string> WeakAreas { get; set; } = [];
    public List<string> StrongAreas { get; set; } = [];
    public DateTime LastPracticedAt { get; set; } = DateTime.UtcNow;
}

public class MentorQuizQuestion
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public int CorrectIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "MCQ"; // "MCQ", "ShortAnswer", "TrueFalse", "PracticalExercise", "CaseStudy"
    public string CognitiveLevel { get; set; } = "Understanding"; // "Recall", "Understanding", "Application", "ProblemSolving"
    public string? CodeSnippet { get; set; }
    public string? ScenarioContext { get; set; }
    public string? Subtopic { get; set; }
    public bool? UserAnswered { get; set; }
    public int? UserAnswerIndex { get; set; }
    public string? UserTextAnswer { get; set; }
    public bool? IsCorrect { get; set; }
}

public class MentorQuiz
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Adaptive"; // "Adaptive", "Beginner", "Intermediate", "Advanced"
    public List<MentorQuizQuestion> Questions { get; set; } = [];
    public int CurrentQuestionIndex { get; set; }
    public int Score { get; set; }
    public int ConceptualScore { get; set; }
    public int PracticalScore { get; set; }
    public bool IsComplete { get; set; }
    public List<string> DetectedWeakAreas { get; set; } = [];
    public string NextMove { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MentorStudyTask
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty; // "Tutorial", "Project", "Practice", "Reading"
    public string? EstimatedTime { get; set; }
    public bool IsCompleted { get; set; }
    public string? FocusArea { get; set; } // "WeakArea", "CoreSkill", "Portfolio"

    /// <summary>
    /// One line on why this task is on the plan, in terms of the learner's own goal.
    ///
    /// Spec 9.4. A plan is a list of instructions until each line says what it is for; without
    /// this the learner has to take on trust that week three belongs where it is. Derived from
    /// the task's focus area and the plan's goal, so it states a relationship that exists rather
    /// than asserting importance.
    /// </summary>
    public string Relevance { get; set; } = string.Empty;

    /// <summary>
    /// Where to go to do this task.
    ///
    /// Spec 9.4b. A search URL built from the task's own title and the plan's subject, not a
    /// specific video: Radar has watched none of these, and a fixed link would rot the way the
    /// 21 dead video ids in the catalogue did. "Here is where to look" is the honest form of a
    /// suggestion nobody has verified.
    /// </summary>
    public string SuggestedSearchUrl { get; set; } = string.Empty;
}

public class MentorStudyWeek
{
    public int WeekNumber { get; set; }
    public string Theme { get; set; } = string.Empty;
    public List<MentorStudyTask> Tasks { get; set; } = [];
    public bool IsCompleted { get; set; }
    public string Milestone { get; set; } = string.Empty;
}

public class MentorStudyPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public int DurationWeeks { get; set; } = 6;
    public List<MentorStudyWeek> Weeks { get; set; } = [];
    public string Status { get; set; } = "Active"; // "Active", "Adjusted", "Completed"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAdjustedAt { get; set; }
}

public class PersonalLearningMemory
{
    public string UserId { get; set; } = string.Empty;
    public string PrimaryGoal { get; set; } = string.Empty;
    public string ActiveSkill { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = "Beginner";
    public List<string> WeakAreas { get; set; } = [];
    public List<string> StrongAreas { get; set; } = [];
    public List<string> RecentMistakes { get; set; } = [];
    public List<TopicMastery> Masteries { get; set; } = [];
    public List<string> CompletedProjects { get; set; } = [];
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
}

public class SpacedRevisionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string IntervalName { get; set; } = "Day 2: Quick Recall"; // "Day 1: Learn", "Day 2: Quick Recall", "Day 4: Practice", "Day 7: Application", "Day 21: Mastery Test"
    public DateTime DueDate { get; set; } = DateTime.UtcNow;
    public string Prompt { get; set; } = string.Empty;
    public string ExpectedTakeaway { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool WasRemembered { get; set; }
}

public class DailyLearningCard
{
    /// <summary>
    /// Set when there is nothing to recommend: no path, and no answered questions to learn from.
    /// </summary>
    /// <remarks>
    /// The card used to fall back to "SQL JOINs" and announce it as a weak area "based on recent
    /// performance", for learners who had answered nothing and were studying something else. A
    /// recommendation with no evidence behind it is worse than no recommendation.
    /// </remarks>
    public string? EmptyReason { get; set; }

    public string Topic { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; } = 20;
    public string Why { get; set; } = string.Empty;
    public int LearnMinutes { get; set; } = 8;
    public int PracticeMinutes { get; set; } = 8;
    public int ApplyMinutes { get; set; } = 4;
    public string NextMove { get; set; } = string.Empty;
    public string? SuggestedPrompt { get; set; }
}

public class ChallengeResult
{
    public string Topic { get; set; } = string.Empty;
    public int ScorePercent { get; set; }
    public int ConceptualScorePercent { get; set; }
    public int PracticalScorePercent { get; set; }
    public List<string> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public string Recommendation { get; set; } = string.Empty;
    public string NextMove { get; set; } = string.Empty;
}

/// <summary>
/// One Mentor chat thread, with every turn that has been taken in it.
/// </summary>
/// <remarks>
/// Chat was the only part of the Mentor that kept nothing. Quizzes and study plans have been
/// stored since they were written, so a learner can come back to them; the conversation lived in
/// the browser tab and was posted back up by the client on every turn. Closing the tab lost the
/// lot - the explanation they were halfway through, and the questions they had asked to get
/// there - and nothing anywhere said that was going to happen.
///
/// The turns are ChatMessage records rather than a new shape, so a thread reopened out of history
/// is the same object the client already renders while a reply is streaming in.
/// </remarks>
public class MentorConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Taken from the learner's first message, shortened at a word boundary.
    /// </summary>
    /// <remarks>
    /// No model call writes this. A list of past conversations is navigation, and navigation that
    /// waits on the provider - or goes blank when the provider is unreachable, which is a state
    /// this product is in often enough to have a sentence for - is worse than a title that is
    /// merely the first thing the learner typed.
    /// </remarks>
    public string Title { get; set; } = string.Empty;

    public List<ChatMessage> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the last turn was appended. The list sorts on this rather than on CreatedAt, so a
    /// thread picked up again a week later comes back to the top, where its reader left it.
    /// </summary>
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A conversation as the list shows it: enough to choose one, without its turns.
/// </summary>
/// <remarks>
/// The turns are nearly the whole of the payload - one tutoring reply runs to a few thousand
/// characters - and this list is what loads every time the Mentor is opened. Sending fifty full
/// transcripts in order to draw fifty titles is the kind of waste that only becomes visible once
/// somebody has used the product for a month, by which time it is their account that is slow.
/// </remarks>
public class MentorConversationSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
}

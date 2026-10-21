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

public class MentorWorkReview
{
    /// <summary>
    /// False when Radar could not actually evaluate the work. Previously a parse failure produced
    /// a fabricated review — verified identical for a security incident report and a banana bread
    /// recipe, down to the score — which is worse than no review: it is misleading feedback on
    /// something the user is trying to improve, and a judgement Radar never made.
    /// </summary>
    public bool WasReviewed { get; set; } = true;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string WorkTitle { get; set; } = string.Empty;
    public string WorkContent { get; set; } = string.Empty;
    public string ReviewType { get; set; } = "Essay"; // "Essay", "Assignment", "Research", "Report", "CV", "BusinessPlan", "Code", "Project"
    public int Score { get; set; } = 75; // Out of 100
    public string Feedback { get; set; } = string.Empty;
    public List<string> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public string MainIssue { get; set; } = string.Empty;
    public List<string> Improvements { get; set; } = [];
    public List<string> Suggestions { get; set; } = [];
    public string NextStep { get; set; } = string.Empty; // "Rewrite paragraph 3 using evidence-analysis approach."
    public int RevisionNumber { get; set; } = 1;
    public int? PreviousScore { get; set; }
    public string? RevisedFromId { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
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
    public int SubmittedReviewsCount { get; set; }
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
    public string Topic { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; } = 20;
    public string Why { get; set; } = "Weak area detected on your roadmap";
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

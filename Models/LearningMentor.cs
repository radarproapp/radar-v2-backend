namespace RadarV2.Models;

public enum MentorMode
{
    Chat,
    Quiz,
    StudyPlan,
    Review
}

public class MentorQuiz
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Topic { get; set; } = string.Empty;
    public List<MentorQuizQuestion> Questions { get; set; } = [];
    public int CurrentQuestionIndex { get; set; }
    public int Score { get; set; }
    public bool IsComplete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MentorQuizQuestion
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public int CorrectIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool? UserAnswered { get; set; }
    public int? UserAnswerIndex { get; set; }
}

public class MentorStudyPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Topic { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public List<MentorStudyWeek> Weeks { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MentorStudyWeek
{
    public int WeekNumber { get; set; }
    public string Theme { get; set; } = string.Empty;
    public List<MentorStudyTask> Tasks { get; set; } = [];
    public bool IsCompleted { get; set; }
}

public class MentorStudyTask
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? EstimatedTime { get; set; }
    public bool IsCompleted { get; set; }
}

public class MentorWorkReview
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkTitle { get; set; } = string.Empty;
    public string WorkContent { get; set; } = string.Empty;
    public string ReviewType { get; set; } = string.Empty;
    public string Feedback { get; set; } = string.Empty;
    public List<string> Strengths { get; set; } = [];
    public List<string> Improvements { get; set; } = [];
    public List<string> Suggestions { get; set; } = [];
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
}

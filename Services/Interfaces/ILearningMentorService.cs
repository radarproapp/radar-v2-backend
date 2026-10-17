using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ILearningMentorService
{
    // 1. Chat & Tutor (Connected to roadmap, goal, Socratic questions, "Your next move")
    Task<ChatMessage> SendMessageAsync(string userId, string message, List<ChatMessage> history);
    IAsyncEnumerable<string> StreamMessageAsync(string userId, string message, List<ChatMessage> history, CancellationToken ct = default);

    // 2. Practice & Quiz (Adaptive difficulty, MCQ/ShortAnswer/Scenario, weak area remediation)
    Task<MentorQuiz> GenerateQuizAsync(string userId, string topic, int questionCount = 5, string difficulty = "Adaptive");
    Task<MentorQuiz?> SubmitAnswerAsync(string userId, string quizId, int questionIndex, int answerIndex, string? textAnswer = null);

    // 3. Study Plan (Dynamic week-by-week, connects goal, deadline, roadmap, weak areas)
    Task<MentorStudyPlan> CreateStudyPlanAsync(string userId, string topic, string goal, int weeks = 6);
    Task<MentorStudyPlan?> GetActiveStudyPlanAsync(string userId);
    Task<MentorStudyPlan?> AdjustStudyPlanAsync(string userId, string planId, string reason);
    Task<bool> ToggleTaskCompletionAsync(string userId, string planId, int weekNumber, int taskIndex);

    // 4. Work Review (Evaluation score, strengths, weaknesses, main issue, specific improvements, next step, revision loop)
    Task<MentorWorkReview> ReviewWorkAsync(string userId, string workTitle, string workContent, string reviewType, string? revisedFromId = null);
    Task<List<MentorWorkReview>> GetUserReviewsAsync(string userId);

    // 5. Personal Learning Memory & Mastery Tracking
    Task<PersonalLearningMemory> GetLearningMemoryAsync(string userId);
    Task<List<TopicMastery>> GetTopicMasteriesAsync(string userId);
    Task<TopicMastery> UpdateTopicMasteryAsync(string userId, string topic, string subtopic, bool isCorrect);

    // 6. Spaced Revision
    Task<List<SpacedRevisionItem>> GetSpacedRevisionsAsync(string userId);
    Task<bool> CompleteSpacedRevisionAsync(string userId, string revisionId, bool wasRemembered);

    // 7. Challenge Mode & Daily Learning
    Task<ChallengeResult> RunChallengeAsync(string userId, string? topic = null);
    Task<DailyLearningCard> GetTodayLearningAsync(string userId);
}

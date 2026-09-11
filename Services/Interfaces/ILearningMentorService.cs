using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ILearningMentorService
{
    // Chat
    Task<ChatMessage> SendMessageAsync(string userId, string message, List<ChatMessage> history);
    IAsyncEnumerable<string> StreamMessageAsync(string userId, string message, List<ChatMessage> history, CancellationToken ct = default);

    // Quiz
    Task<MentorQuiz> GenerateQuizAsync(string userId, string topic, int questionCount = 5);
    Task<MentorQuiz> SubmitAnswerAsync(string quizId, int questionIndex, int answerIndex);

    // Study Plan
    Task<MentorStudyPlan> CreateStudyPlanAsync(string userId, string topic, string goal);
    Task<MentorStudyPlan?> GetActiveStudyPlanAsync(string userId);

    // Work Review
    Task<MentorWorkReview> ReviewWorkAsync(string userId, string workTitle, string workContent, string reviewType);
}

using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IRoadmapService
{
    Task<List<GrowthRoadmap>> GetUserRoadmapsAsync(string userId);
    Task<GrowthRoadmap?> GetActiveRoadmapAsync(string userId);
    Task<GrowthRoadmap> CreateRoadmapAsync(string userId, string goal);

    /// <summary>
    /// Replaces the user's active roadmap with one whose modules and lessons are generated from
    /// their profile rather than picked from a template. Falls back to the template roadmap when
    /// the AI engine is unavailable or budget-capped.
    /// </summary>
    Task<GrowthRoadmap> CreateAiRoadmapAsync(string userId, UserProfile profile, CancellationToken ct = default);
    Task CompleteLessonAsync(string userId, string roadmapId, string moduleId, string lessonId);
    Task CompleteResourceAsync(string userId, string roadmapId, string resourceId);
    Task CompleteExerciseAsync(string userId, string roadmapId, string resourceId);
    Task<GrowthRoadmap?> RecordQuizResultAsync(string userId, string roadmapId, string topic, string stage, int scorePercent);
    Task SetRoadmapStageAsync(string userId, string roadmapId, string stage);
    Task AddTopicToRoadmapAsync(string userId, string roadmapId, string topic);

    /// <summary>
    /// Adds one section to the learner's path, built from what they said they want rather than
    /// inferred from their degree. Creates an empty active path first if they have none.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="CreateAiRoadmapAsync"/>. That reads the profile and returns a
    /// finished four-stage pathway, so a Computer Science student was handed "Foundations of
    /// Computer Science / Core concepts and vocabulary in Computer Science" without ever being
    /// asked what they wanted to learn. A path is built a section at a time, from answers.
    /// </remarks>
    Task<GrowthRoadmap> AddSectionAsync(
        string userId,
        UserProfile profile,
        string subject,
        string level,
        string? outcome,
        CancellationToken ct = default);
    Task<int> GetProgressPercentAsync(string userId, string roadmapId);
}

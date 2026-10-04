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
    Task AddTopicToRoadmapAsync(string userId, string roadmapId, string topic);
    Task<int> GetProgressPercentAsync(string userId, string roadmapId);
}

using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IRoadmapService
{
    Task<List<GrowthRoadmap>> GetUserRoadmapsAsync(string userId);
    Task<GrowthRoadmap?> GetActiveRoadmapAsync(string userId);
    Task<GrowthRoadmap> CreateRoadmapAsync(string userId, string goal);
    Task CompleteLessonAsync(string userId, string roadmapId, string moduleId, string lessonId);
    Task AddTopicToRoadmapAsync(string userId, string roadmapId, string topic);
    Task<int> GetProgressPercentAsync(string userId, string roadmapId);
}

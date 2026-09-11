using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ISourceService
{
    Task<SourceProfile?> GetSourceAsync(string sourceId);
    Task<List<SourceProfile>> GetSourcesAsync(string userId);
    Task ToggleFollowAsync(string userId, string sourceId);
    Task ToggleWeeklyBriefAsync(string userId, string sourceId);
    Task TogglePrioritiseAsync(string userId, string sourceId);
}

public interface ITopicService
{
    Task<TopicProfile?> GetTopicAsync(string topicId);
    Task<List<TopicProfile>> GetTopicsAsync(string userId);
    Task ToggleFollowAsync(string userId, string topicId);
    Task ToggleAlertAsync(string userId, string topicId, int alertIndex);
}

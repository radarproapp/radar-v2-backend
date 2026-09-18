using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IIntelligenceFeedService
{
    Task<List<ContentItem>> GetFeedAsync(UserProfile profile, ContentType? filterType = null, int page = 1, int pageSize = 20);
    Task<ContentItem?> GetByIdAsync(string id);
    Task SaveItemAsync(string userId, string contentItemId);
    Task UnsaveItemAsync(string userId, string contentItemId);
    Task DismissItemAsync(string userId, string contentItemId);
    Task<List<ContentItem>> GetSavedItemsAsync(string userId);
    Task<List<ContentItem>> SearchAsync(string query, ContentType? type = null);
}

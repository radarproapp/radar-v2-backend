using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IIntelligenceFeedService
{
    /// <summary>
    /// Offset-paginated feed. Kept for callers that jump to a numbered page; new callers should use
    /// <see cref="GetFeedPageAsync"/>, which does not shift when items are ingested mid-paging.
    /// </summary>
    Task<List<ContentItem>> GetFeedAsync(UserProfile profile, ContentType? filterType = null, int page = 1, int pageSize = 20);

    /// <summary>
    /// One keyset-paginated page of the feed. Ordering is by publish time (descending, id as the
    /// tiebreaker) — see Models/Cursor — with personalisation ranking applied *within* each page.
    /// </summary>
    Task<PagedResult<ContentItem>> GetFeedPageAsync(UserProfile profile, ContentType? filterType, string? cursor, int limit, CancellationToken ct = default);

    Task<ContentItem?> GetByIdAsync(string id);
    Task SaveItemAsync(string userId, string contentItemId);
    Task UnsaveItemAsync(string userId, string contentItemId);
    Task DismissItemAsync(string userId, string contentItemId);
    Task<List<ContentItem>> GetSavedItemsAsync(string userId);
    Task<List<ContentItem>> SearchAsync(string query, ContentType? type = null);
}

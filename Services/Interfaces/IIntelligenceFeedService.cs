using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IIntelligenceFeedService
{
    /// <summary>
    /// Offset-paginated feed. Kept for callers that jump to a numbered page; new callers should use
    /// <see cref="GetFeedPageAsync"/>, which does not shift when items are ingested mid-paging.
    /// </summary>
    /// <param name="acrossSurfaces">
    /// Whether to look past the Intelligence feed into everything Radar holds.
    /// </param>
    /// <remarks>
    /// The feed is news and podcasts; papers, policy and essays belong to Research Discovery.
    /// The weekly brief is neither - it is a summary of the week across both - so it is the one
    /// caller that asks for content the feed itself would not show.
    /// </remarks>
    Task<List<ContentItem>> GetFeedAsync(UserProfile profile, ContentType? filterType = null, int page = 1, int pageSize = 20, bool acrossSurfaces = false);

    /// <summary>
    /// One keyset-paginated page of the feed. Ordering is by publish time (descending, id as the
    /// tiebreaker) — see Models/Cursor — with personalisation ranking applied *within* each page.
    /// </summary>
    Task<PagedResult<ContentItem>> GetFeedPageAsync(UserProfile profile, ContentType? filterType, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// The research side of the corpus for this reader: papers, policy, essays and long-form
    /// analysis, matched by the same rule the feed uses.
    /// </summary>
    Task<List<ContentItem>> GetResearchLibraryAsync(UserProfile profile, int limit = 20, CancellationToken ct = default);

    Task<ContentItem?> GetByIdAsync(string id);
    Task SaveItemAsync(string userId, string contentItemId);
    Task UnsaveItemAsync(string userId, string contentItemId);
    Task DismissItemAsync(string userId, string contentItemId);

    /// <summary>Undoes every dismissal for this user. Returns how many were restored.</summary>
    Task<long> RestoreDismissedAsync(string userId);
    Task<List<ContentItem>> GetSavedItemsAsync(string userId);
    Task<List<ContentItem>> SearchAsync(string query, ContentType? type = null);
}

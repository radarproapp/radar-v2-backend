namespace RadarV2.Models;

/// <summary>
/// A cached YouTube Data API search.
///
/// Caching is not an optimisation here, it is what makes the feature affordable. A `search.list`
/// call costs 100 quota units and the free daily allowance is 10,000, so roughly 100 searches a
/// day across all users. Lesson titles repeat across every learner on the same path, so one cached
/// result serves all of them, and the quota is spent on genuinely new topics instead.
/// </summary>
public class YouTubeSearchCacheDoc
{
    /// <summary>Hash of the query plus any channel scope.</summary>
    public string Id { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;
    public string? ChannelHandle { get; set; }
    public List<YouTubeVideoRef> Results { get; set; } = [];
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set when the search ran but YouTube returned nothing. Stored so a dead query is not retried
    /// on every page load, burning quota to learn the same thing again.
    /// </summary>
    public bool WasEmpty { get; set; }
}

public class YouTubeVideoRef
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ChannelTitle { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public DateTime PublishedAt { get; set; }

    /// <summary>The watch page. Embeds use /embed/{id}, which is YouTube's supported player.</summary>
    public string Url => $"https://www.youtube.com/watch?v={VideoId}";
    public string EmbedUrl => $"https://www.youtube.com/embed/{VideoId}";
}

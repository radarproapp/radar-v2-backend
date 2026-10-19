using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Finds real videos for a learning topic through the YouTube Data API, and caches the answer.
///
/// This exists so Learn can stop shipping hand-written video IDs. A fixed ID rots silently, cannot
/// be reviewed for accuracy, and only ever covers the domains somebody wrote out by hand — which
/// is how a cybersecurity learner ended up with an Excel curriculum and a nurse got nothing at all.
///
/// Two constraints shape the design:
///
/// 1. Quota. `search.list` costs 100 units against a 10,000/day free allowance, so about 100
///    searches a day for the whole product. Every result is cached in Mongo and shared across all
///    users, because lesson titles repeat across everyone on the same path. Empty results are
///    cached too, so a dead query is not re-learned daily at 100 units a time.
///
/// 2. Terms of service. This uses the official API and links to watch pages or the supported
///    /embed/ player. It does not scrape results or lift transcripts, which is where YouTube's
///    terms get restrictive.
///
/// With no key configured the service returns nothing and callers fall back to search links, so
/// Learn degrades to "here is where to look" rather than breaking.
/// </summary>
public class YouTubeSearchService
{
    private readonly RadarDatabase _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<YouTubeSearchService> _log;

    public YouTubeSearchService(
        RadarDatabase db,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<YouTubeSearchService> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["YouTube:ApiKey"]);

    /// <summary>Cached results only. Never calls the API, so it is safe on a request path.</summary>
    public async Task<List<YouTubeVideoRef>> GetCachedAsync(string query, string? channelId = null, CancellationToken ct = default)
    {
        var doc = await _db.YouTubeSearches
            .Find(d => d.Id == CacheKey(query, channelId))
            .FirstOrDefaultAsync(ct);

        var ttlDays = _config.GetValue("YouTube:CacheDays", 30);
        if (doc is null || doc.CachedAt < DateTime.UtcNow.AddDays(-ttlDays)) return [];

        return doc.Results;
    }

    /// <summary>
    /// Searches and caches. Call this off the request path — it spends quota and waits on a network
    /// round trip.
    /// </summary>
    public async Task<List<YouTubeVideoRef>> SearchAndCacheAsync(
        string query, string? channelId = null, int take = 3, CancellationToken ct = default)
    {
        var apiKey = _config["YouTube:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        var cached = await GetCachedAsync(query, channelId, ct);
        if (cached.Count > 0) return cached;

        try
        {
            var url = "https://www.googleapis.com/youtube/v3/search"
                + "?part=snippet&type=video&videoEmbeddable=true&safeSearch=moderate"
                + $"&maxResults={Math.Clamp(take, 1, 10)}"
                + $"&q={Uri.EscapeDataString(query)}"
                + (string.IsNullOrWhiteSpace(channelId) ? "" : $"&channelId={Uri.EscapeDataString(channelId)}")
                + $"&key={Uri.EscapeDataString(apiKey)}";

            var client = _httpFactory.CreateClient("YouTube");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            using var response = await client.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                // 403 here is almost always quota exhaustion or a restricted key. Say which, because
                // silently returning nothing looks identical to "no results for that topic".
                _log.LogWarning("YouTube search for {Query} returned {Status}.", query, response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var results = Parse(json, take);

            await _db.YouTubeSearches.ReplaceOneAsync(
                d => d.Id == CacheKey(query, channelId),
                new YouTubeSearchCacheDoc
                {
                    Id = CacheKey(query, channelId),
                    Query = query,
                    ChannelHandle = channelId,
                    Results = results,
                    WasEmpty = results.Count == 0,
                    CachedAt = DateTime.UtcNow,
                },
                new ReplaceOptions { IsUpsert = true },
                cts.Token);

            return results;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "YouTube search failed for {Query}", query);
            return [];
        }
    }

    private static List<YouTubeVideoRef> Parse(string json, int take)
    {
        var results = new List<YouTubeVideoRef>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return results;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || !id.TryGetProperty("videoId", out var videoId)) continue;
            if (!item.TryGetProperty("snippet", out var snippet)) continue;

            results.Add(new YouTubeVideoRef
            {
                VideoId = videoId.GetString() ?? string.Empty,
                Title = Text(snippet, "title"),
                Description = Text(snippet, "description"),
                ChannelTitle = Text(snippet, "channelTitle"),
                ThumbnailUrl = snippet.TryGetProperty("thumbnails", out var thumbs)
                    && thumbs.TryGetProperty("high", out var high)
                        ? Text(high, "url")
                        : null,
                PublishedAt = snippet.TryGetProperty("publishedAt", out var published)
                    && published.TryGetDateTime(out var when)
                        ? when
                        : DateTime.UtcNow,
            });

            if (results.Count >= take) break;
        }

        return results;
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string CacheKey(string query, string? channelId)
    {
        var raw = $"{query.Trim().ToLowerInvariant()}|{channelId ?? "any"}";
        return "yt:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }
}

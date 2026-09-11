using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Fetches podcast episodes from PodcastIndex API.
/// Auth: X-Auth-Key + X-Auth-Date + SHA-1 hash Authorization header (computed per request).
/// Quota: no hard limit; fair use. Max 5 episodes per search term per cycle.
/// </summary>
public class PodcastIndexIngestionService
{
    private const int MaxEpisodesPerFeed = 5;
    private const int MaxFeedsPerSearch  = 3;

    private static readonly (string Query, ContentLayer Layer, int Tier)[] SearchTerms =
    [
        ("Africa business economy",         ContentLayer.Finance,     2),
        ("Nigeria tech startup Africa",     ContentLayer.Ideas,       2),
        ("climate change environment",      ContentLayer.Environment, 1),
        ("Africa health medicine",          ContentLayer.Medicine,    2),
        ("African history culture",         ContentLayer.History,     2),
        ("Afrobeats music Africa",          ContentLayer.Music,       2),
        ("Africa education learning",       ContentLayer.Education,   2),
        ("Nigeria football Africa sports",  ContentLayer.Sports,      2),
        ("African philosophy ethics",       ContentLayer.Philosophy,  2),
        ("Africa technology innovation",    ContentLayer.Science,     2),
        ("personal finance Africa",         ContentLayer.Finance,     2),
        ("Nigeria politics governance",     ContentLayer.Policy,      2),
    ];

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly ILogger<PodcastIndexIngestionService> _log;

    public PodcastIndexIngestionService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<PodcastIndexIngestionService> log)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _log         = log;
    }

    public async Task<List<RawFeedItem>> FetchEpisodesAsync(CancellationToken ct)
    {
        var apiKey    = _config["PodcastIndex:ApiKey"];
        var apiSecret = _config["PodcastIndex:ApiSecret"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            _log.LogDebug("PodcastIndex credentials not configured — skipping.");
            return [];
        }

        var all = new List<RawFeedItem>();

        foreach (var (query, layer, tier) in SearchTerms)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var feeds = await SearchFeedsByTermAsync(apiKey, apiSecret, query, ct);
                foreach (var feedResult in feeds)
                {
                    // Fetch authoritative metadata from /podcasts/byfeedid
                    var feed     = await GetFeedMetadataAsync(apiKey, apiSecret, feedResult, ct);
                    var episodes = await FetchEpisodesByFeedIdAsync(apiKey, apiSecret, feed, query, layer, tier, ct);
                    all.AddRange(episodes);
                    await Task.Delay(350, ct);
                }
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning("PodcastIndex search '{Query}' failed: {Message}", query, ex.Message);
            }
        }

        _log.LogInformation("PodcastIndex: fetched {Count} episodes.", all.Count);
        return all;
    }

    // Represents metadata fetched from /podcasts/byfeedid
    private record PodcastFeedMeta(long Id, string Title, string? Image, string? RssUrl);

    private async Task<List<PodcastFeedMeta>> SearchFeedsByTermAsync(string apiKey, string secret, string query, CancellationToken ct)
    {
        var client = BuildClient(apiKey, secret);
        var url    = $"search/byterm?q={Uri.EscapeDataString(query)}&max={MaxFeedsPerSearch * 2}";
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("feeds", out var feeds)) return [];

        return feeds.EnumerateArray()
            .Select(f =>
            {
                var id    = f.TryGetProperty("id",    out var i) ? i.GetInt64()         : 0L;
                var title = f.TryGetProperty("title", out var t) ? t.GetString() ?? ""  : "";
                var image = f.TryGetProperty("image", out var im) ? im.GetString()       : null;
                var rss   = f.TryGetProperty("url",   out var u)  ? u.GetString()        : null;
                return new PodcastFeedMeta(id, title, image, rss);
            })
            .Where(f => f.Id > 0 && !string.IsNullOrWhiteSpace(f.Title))
            .Take(MaxFeedsPerSearch)
            .ToList();
    }

    /// <summary>
    /// Fetches authoritative feed metadata from /podcasts/byfeedid.
    /// Supplements the search result with verified title, image, and RSS URL.
    /// </summary>
    private async Task<PodcastFeedMeta> GetFeedMetadataAsync(string apiKey, string secret, PodcastFeedMeta searchResult, CancellationToken ct)
    {
        try
        {
            var client = BuildClient(apiKey, secret);
            var url    = $"podcasts/byfeedid?id={searchResult.Id}";
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return searchResult;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("feed", out var feed)) return searchResult;

            var title = feed.TryGetProperty("title", out var t) ? t.GetString() ?? searchResult.Title : searchResult.Title;
            var image = feed.TryGetProperty("image", out var img) ? img.GetString() ?? searchResult.Image : searchResult.Image;
            var rss   = feed.TryGetProperty("url",   out var u)   ? u.GetString()  ?? searchResult.RssUrl : searchResult.RssUrl;
            return searchResult with { Title = title, Image = image, RssUrl = rss };
        }
        catch
        {
            return searchResult;
        }
    }

    private async Task<List<RawFeedItem>> FetchEpisodesByFeedIdAsync(
        string apiKey, string secret, PodcastFeedMeta feed,
        string query, ContentLayer layer, int tier, CancellationToken ct)
    {
        var client = BuildClient(apiKey, secret);
        var url    = $"episodes/byfeedid?id={feed.Id}&max={MaxEpisodesPerFeed}";
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return [];

        var result = new List<RawFeedItem>();
        foreach (var ep in items.EnumerateArray())
        {
            var enclosureUrl = ep.TryGetProperty("enclosureUrl", out var eu) ? eu.GetString() : null;
            var pageUrl      = ep.TryGetProperty("link",         out var lu) ? lu.GetString() : enclosureUrl;
            var epUrl        = pageUrl ?? enclosureUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(epUrl)) continue;

            var title    = ep.TryGetProperty("title",       out var t)   ? t.GetString()   ?? string.Empty : string.Empty;
            var desc     = ep.TryGetProperty("description", out var d)   ? StripHtml(d.GetString() ?? string.Empty) : string.Empty;
            var duration = ep.TryGetProperty("duration",    out var dur) ? dur.GetInt32()  : (int?)null;
            var epImage  = ep.TryGetProperty("image",       out var img) ? img.GetString() : feed.Image;

            DateTime publishedAt = DateTime.UtcNow;
            if (ep.TryGetProperty("datePublished", out var dp))
                publishedAt = DateTimeOffset.FromUnixTimeSeconds(dp.GetInt64()).UtcDateTime;

            result.Add(new RawFeedItem
            {
                SourceId        = $"podcastindex:{feed.Id}",
                SourceName      = feed.Title,
                Layer           = layer,
                Tier            = tier,
                DefaultType     = ContentType.Podcast,
                Topics          = [.. query.Split(' ').Take(4)],
                Title           = title,
                Url             = epUrl,
                UrlHash         = RssIngestionService.HashUrl(epUrl),
                Description     = desc,
                PublishedAt     = publishedAt,
                AudioUrl        = enclosureUrl,
                DurationSeconds = duration,
                PodcastFeedId   = feed.Id.ToString(),
            });
        }
        return result;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private HttpClient BuildClient(string apiKey, string apiSecret)
    {
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var hashInput     = apiKey + apiSecret + unixTimestamp;
        var hashBytes     = SHA1.HashData(Encoding.UTF8.GetBytes(hashInput));
        var authHash      = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var client = _httpFactory.CreateClient("PodcastIndex");
        // Clone headers per-request since X-Auth-Date/Authorization are time-sensitive.
        // CreateClient returns a new instance each time — headers are set fresh here.
        client.DefaultRequestHeaders.Remove("X-Auth-Key");
        client.DefaultRequestHeaders.Remove("X-Auth-Date");
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Add("X-Auth-Key",  apiKey);
        client.DefaultRequestHeaders.Add("X-Auth-Date", unixTimestamp);
        client.DefaultRequestHeaders.Add("Authorization", authHash);
        return client;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }
}

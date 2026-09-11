using System.Text.Json;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using MongoDB.Driver;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Fetches news articles from Mediastack.
/// Free tier: 100 req/month, HTTP only, ~30-min publish delay.
/// Monthly quota is tracked in MongoDB so the budget is never exceeded.
/// </summary>
public class MediastackIngestionService
{
    // One request per cluster per cycle — 10 clusters × max 9 cycles/month = 90 req ≤ 100 cap.
    private const int MonthlyRequestCap = 90;
    private const int ItemsPerRequest   = 25;

    // Topic clusters: each entry is one Mediastack /news request.
    // keywords → ContentLayer assignment for every item in that result set.
    private static readonly (string Keywords, string Countries, ContentLayer Layer, int Tier)[] Clusters =
    [
        ("climate change,renewable energy,net zero",              "ng,za,gh,ke,et", ContentLayer.Environment, 2),
        ("Nigeria politics,Nigeria economy,Nigeria government",   "ng",             ContentLayer.Policy,      2),
        ("Africa governance,African Union,ECOWAS",               "ng,za,gh,ke",    ContentLayer.Policy,      2),
        ("Nigeria fintech,Africa finance,capital markets Nigeria","ng,za",          ContentLayer.Finance,     2),
        ("Africa health,Nigeria health,disease outbreak Africa",  "ng,za,ke,et",    ContentLayer.Medicine,    2),
        ("science discovery,scientific research,breakthrough",    "",               ContentLayer.Science,     2),
        ("education Africa,Nigeria schools,university admission", "ng,gh,ke",       ContentLayer.Education,   2),
        ("Nigeria football,Africa sports,Super Eagles",          "ng,za,gh",       ContentLayer.Sports,      2),
        ("Afrobeats music,Africa entertainment,Nollywood",       "ng,za,gh",       ContentLayer.Music,       2),
        ("Nigeria agriculture,food security Africa,farming",     "ng,et,ke",       ContentLayer.Agriculture, 2),
    ];

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly RadarDatabase      _db;
    private readonly ILogger<MediastackIngestionService> _log;

    public MediastackIngestionService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        ILogger<MediastackIngestionService> log)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _db          = db;
        _log         = log;
    }

    public async Task<List<RawFeedItem>> FetchAllClustersAsync(CancellationToken ct)
    {
        var apiKey = _config["Mediastack:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogDebug("Mediastack API key not configured — skipping.");
            return [];
        }

        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var quota = await GetOrCreateQuotaAsync("mediastack", month, ct);

        if (quota.RequestCount >= MonthlyRequestCap)
        {
            _log.LogWarning("Mediastack monthly quota exhausted ({Count}/{Cap}) — skipping until next month.",
                quota.RequestCount, MonthlyRequestCap);
            return [];
        }

        var remaining = MonthlyRequestCap - quota.RequestCount;
        var toRun     = Clusters.Take(remaining).ToArray();

        var all = new List<RawFeedItem>();
        foreach (var (keywords, countries, layer, tier) in toRun)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var items = await FetchClusterAsync(apiKey, keywords, countries, layer, tier, ct);
                all.AddRange(items);
                await IncrementQuotaAsync("mediastack", month, 1, 0m, ct);
                await Task.Delay(500, ct); // polite pacing
            }
            catch (Exception ex)
            {
                _log.LogWarning("Mediastack cluster '{Keywords}' failed: {Message}", keywords, ex.Message);
            }
        }

        _log.LogInformation("Mediastack: fetched {Count} items across {Clusters} clusters.", all.Count, toRun.Length);
        return all;
    }

    private async Task<List<RawFeedItem>> FetchClusterAsync(
        string apiKey, string keywords, string countries, ContentLayer layer, int tier, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("Mediastack");

        var qs = $"news?access_key={Uri.EscapeDataString(apiKey)}" +
                 $"&keywords={Uri.EscapeDataString(keywords)}" +
                 $"&languages=en" +
                 (string.IsNullOrEmpty(countries) ? "" : $"&countries={countries}") +
                 $"&sort=published_desc&limit={ItemsPerRequest}";

        using var response = await client.GetAsync(qs, ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("Mediastack returned {Status} for cluster '{Keywords}'", response.StatusCode, keywords);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

        var items = new List<RawFeedItem>();
        foreach (var article in data.EnumerateArray())
        {
            var url = article.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(url)) continue;

            var title       = article.TryGetProperty("title",       out var t)  ? t.GetString()  ?? string.Empty : string.Empty;
            var description = article.TryGetProperty("description", out var d)  ? d.GetString()  ?? string.Empty : string.Empty;
            var source      = article.TryGetProperty("source",      out var s)  ? s.GetString()  ?? string.Empty : string.Empty;
            var publishedAt = DateTime.UtcNow;
            if (article.TryGetProperty("published_at", out var pa) && DateTime.TryParse(pa.GetString(), out var parsed))
                publishedAt = parsed.ToUniversalTime();

            items.Add(new RawFeedItem
            {
                SourceId    = $"mediastack:{layer}",
                SourceName  = source,
                Layer       = layer,
                Tier        = tier,
                DefaultType = ContentType.Article,
                Topics      = [.. keywords.Split(',').Select(k => k.Trim())],
                Title       = title,
                Url         = url,
                UrlHash     = RssIngestionService.HashUrl(url),
                Description = description,
                PublishedAt = publishedAt,
            });
        }
        return items;
    }

    // ── Quota helpers ─────────────────────────────────────────────────────────

    private async Task<IngestionQuotaDoc> GetOrCreateQuotaAsync(string service, string month, CancellationToken ct)
    {
        var id     = $"{service}:{month}";
        var filter = Builders<IngestionQuotaDoc>.Filter.Eq(q => q.Id, id);
        var doc    = await _db.IngestionQuotas.Find(filter).FirstOrDefaultAsync(ct);
        if (doc is not null) return doc;

        doc = new IngestionQuotaDoc { Id = id, Service = service, Month = month };
        await _db.IngestionQuotas.InsertOneAsync(doc, cancellationToken: ct);
        return doc;
    }

    private async Task IncrementQuotaAsync(string service, string month, int requests, decimal spend, CancellationToken ct)
    {
        var id     = $"{service}:{month}";
        var filter = Builders<IngestionQuotaDoc>.Filter.Eq(q => q.Id, id);
        var update = Builders<IngestionQuotaDoc>.Update
            .Inc(q => q.RequestCount, requests)
            .Inc(q => q.SpendUsd, spend)
            .Set(q => q.UpdatedAt, DateTime.UtcNow);
        await _db.IngestionQuotas.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}

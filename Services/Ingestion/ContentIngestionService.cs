using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

public class ContentIngestionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentIngestionService> _log;

    // RSS/OpenAlex runs every 6h. Mediastack and PodcastIndex run every 24h.
    private static readonly TimeSpan RssInterval       = TimeSpan.FromHours(6);
    private static readonly TimeSpan SlowSourceInterval = TimeSpan.FromHours(24);

    private const int MaxEnrichPerSource = 5;

    private DateTime _lastSlowCycle = DateTime.MinValue;

    public ContentIngestionService(IServiceScopeFactory scopeFactory, ILogger<ContentIngestionService> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Content ingestion service started.");
        await RunCycleAsync(ct);

        using var timer = new PeriodicTimer(RssInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            await RunCycleAsync(ct);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        _log.LogInformation("Ingestion cycle starting at {Time}", DateTime.UtcNow);
        var totalNew = 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp      = scope.ServiceProvider;
        var db      = sp.GetRequiredService<RadarDatabase>();
        var rss     = sp.GetRequiredService<RssIngestionService>();
        var enricher = sp.GetRequiredService<ContentEnricherService>();

        // ── Layer 1: RSS / OpenAlex (every cycle) ──────────────────────────
        totalNew += await RunFetchBatchAsync(db, enricher,
            await FetchRssAllSourcesAsync(rss, ct), ct);

        // ── Layer 2: Mediastack + PodcastIndex (once per 24h) ──────────────
        var runSlowSources = (DateTime.UtcNow - _lastSlowCycle) >= SlowSourceInterval;
        if (runSlowSources)
        {
            var mediastack    = sp.GetRequiredService<MediastackIngestionService>();
            var podcastIndex  = sp.GetRequiredService<PodcastIndexIngestionService>();
            var taddy         = sp.GetRequiredService<TaddyClient>();
            var groqWhisper   = sp.GetRequiredService<GroqWhisperClient>();

            var mediastackItems = await mediastack.FetchAllClustersAsync(ct);
            totalNew += await RunFetchBatchAsync(db, enricher, mediastackItems, ct);

                var podcastItems = await podcastIndex.FetchEpisodesAsync(ct);

            // Supplement with Taddy searchForTerm — transcript-first discovery
            if (taddy.IsConfigured)
            {
                var taddyItems = await FetchTaddyEpisodesAsync(taddy, ct);
                podcastItems.AddRange(taddyItems);
            }

            var enrichedPodcasts = await EnrichPodcastTranscriptsAsync(podcastItems, taddy, groqWhisper, ct);
            totalNew += await RunFetchBatchAsync(db, enricher, enrichedPodcasts, ct);

            _lastSlowCycle = DateTime.UtcNow;
        }

        _log.LogInformation("Ingestion cycle complete. {Total} new items added.", totalNew);
    }

    // ── RSS batch ─────────────────────────────────────────────────────────────

    private async Task<List<RawFeedItem>> FetchRssAllSourcesAsync(RssIngestionService rss, CancellationToken ct)
    {
        var all = new List<RawFeedItem>();
        foreach (var source in SourceRegistry.All.Where(s => s.IsEnabled))
        {
            if (ct.IsCancellationRequested) break;
            try { all.AddRange(await rss.FetchAsync(source, ct)); }
            catch (Exception ex) { _log.LogWarning("RSS fetch failed for {Source}: {Message}", source.Name, ex.Message); }
        }
        return all;
    }

    // ── Generic enrich + persist batch ───────────────────────────────────────

    private async Task<int> RunFetchBatchAsync(
        RadarDatabase db, ContentEnricherService enricher,
        List<RawFeedItem> rawItems, CancellationToken ct)
    {
        var newItems = await FilterNewItemsAsync(db, rawItems, ct);
        // Group by source, limit per source to control costs
        var toEnrich = newItems
            .GroupBy(i => i.SourceId)
            .SelectMany(g => g.Take(MaxEnrichPerSource))
            .ToList();

        int count = 0;
        foreach (var raw in toEnrich)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var item = await enricher.EnrichAsync(raw, ct);
                await db.ContentItems.InsertOneAsync(item, cancellationToken: ct);
                count++;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning("Enrichment/persist failed for '{Title}': {Message}", raw.Title, ex.Message);
            }
        }
        return count;
    }

    // ── Taddy episode discovery via searchForTerm ────────────────────────────

    private static readonly (string Term, ContentLayer Layer)[] TaddySearchTerms =
    [
        ("Africa business economy startup",      ContentLayer.Finance),
        ("Nigeria technology innovation",        ContentLayer.Ideas),
        ("climate change renewable Africa",      ContentLayer.Environment),
        ("Africa history culture society",       ContentLayer.History),
        ("personal finance investing Africa",    ContentLayer.Finance),
        ("African music Afrobeats industry",     ContentLayer.Music),
        ("Nigeria politics governance",          ContentLayer.Policy),
        ("Africa education learning",            ContentLayer.Education),
    ];

    private async Task<List<RawFeedItem>> FetchTaddyEpisodesAsync(TaddyClient taddy, CancellationToken ct)
    {
        var all = new List<RawFeedItem>();
        foreach (var (term, layer) in TaddySearchTerms)
        {
            if (ct.IsCancellationRequested) break;
            var episodes = await taddy.SearchEpisodesAsync(term, maxResults: 5, ct: ct);
            foreach (var ep in episodes)
            {
                if (string.IsNullOrWhiteSpace(ep.AudioUrl)) continue;
                var url  = ep.AudioUrl;
                var hash = RssIngestionService.HashUrl(url);
                all.Add(new RawFeedItem
                {
                    SourceId        = $"taddy:{ep.Uuid}",
                    SourceName      = ep.PodcastName,
                    Layer           = layer,
                    Tier            = 2,
                    DefaultType     = ContentType.Podcast,
                    Topics          = [.. term.Split(' ').Take(3)],
                    Title           = ep.Name,
                    Url             = url,
                    UrlHash         = hash,
                    Description     = ep.Transcript.Length > 500 ? ep.Transcript[..500] : ep.Transcript,
                    PublishedAt     = ep.PublishedAt,
                    AudioUrl        = ep.AudioUrl,
                    TranscriptText  = ep.Transcript,
                    DurationSeconds = ep.DurationSeconds,
                });
            }
            await Task.Delay(400, ct);
        }
        return all;
    }

    // ── Transcript enrichment for podcast items ───────────────────────────────

    private async Task<List<RawFeedItem>> EnrichPodcastTranscriptsAsync(
        List<RawFeedItem> episodes,
        TaddyClient taddy,
        GroqWhisperClient groq,
        CancellationToken ct)
    {
        int groqUsed = 0;
        foreach (var ep in episodes)
        {
            if (ct.IsCancellationRequested) break;
            if (!string.IsNullOrWhiteSpace(ep.TranscriptText)) continue;

            // Try Taddy first (has transcript database)
            if (taddy.IsConfigured && !string.IsNullOrWhiteSpace(ep.Url))
            {
                var transcript = await taddy.GetEpisodeTranscriptAsync(ep.Url, isUrl: true, ct);
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    ep.TranscriptText = transcript;
                    continue;
                }
            }

            // Fallback: Groq Whisper from audioUrl — budget-capped per cycle
            if (groq.IsConfigured &&
                !string.IsNullOrWhiteSpace(ep.AudioUrl) &&
                groqUsed < GroqWhisperClient.MaxTranscriptionsPerCycle)
            {
                var transcript = await groq.TranscribeUrlAsync(ep.AudioUrl, ct: ct);
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    ep.TranscriptText = transcript;
                    groqUsed++;
                }
            }
        }
        return episodes;
    }

    private static async Task<List<RawFeedItem>> FilterNewItemsAsync(
        RadarDatabase db, List<RawFeedItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];

        var hashes = items.Select(i => i.UrlHash).ToList();

        var existing = await db.ContentItems
            .Find(Builders<ContentItem>.Filter.In(c => c.UrlHash, hashes))
            .Project(c => c.UrlHash)
            .ToListAsync(ct);

        var existingSet = existing.ToHashSet();
        return items.Where(i => !existingSet.Contains(i.UrlHash)).ToList();
    }
}

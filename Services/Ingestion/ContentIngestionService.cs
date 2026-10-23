using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
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

    /// <summary>How many items are enriched at once. Four keeps a cycle to minutes, not an hour.</summary>
    private const int EnrichConcurrency = 4;

    private DateTime _lastSlowCycle = DateTime.MinValue;

    // Serialises the periodic timer and on-demand (cron) triggers so cycles never overlap.
    private static readonly SemaphoreSlim _cycleLock = new(1, 1);

    public ContentIngestionService(IServiceScopeFactory scopeFactory, ILogger<ContentIngestionService> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Content ingestion service started.");

        // Allow application to finish startup, bind ports, and serve health checks / early user requests cleanly
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await BackfillTopicsAsync(ct);
        await ReconcileLayersAsync(ct);

        // Fetch before backfilling. The enrichment backfill is up to 120 sequential model calls —
        // six to ten minutes — and running it here put a repair of old items in front of getting
        // new ones, so twenty newly added sources sat unfetched while the queue drained. The
        // cycle ends with the same backfill, so nothing is skipped by dropping it from here.
        await RunCycleOnceAsync(ct);

        using var timer = new PeriodicTimer(RssInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            await RunCycleOnceAsync(ct);
    }

    /// <summary>
    /// Runs one ingestion cycle on demand — used by the scheduled cron job (and at startup). Skips
    /// if a cycle is already in flight so the timer and an external trigger can't run at once.
    /// </summary>
    public async Task<bool> RunCycleOnceAsync(CancellationToken ct = default)
    {
        if (!await _cycleLock.WaitAsync(0, ct))
        {
            _log.LogInformation("Ingestion cycle already running; skipping this trigger.");
            return false;
        }
        try
        {
            await RunCycleCoreAsync(ct);
            return true;
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    // ── One-time topic backfill ───────────────────────────────────────────────

    /// <summary>
    /// Items ingested before topics were persisted onto the item carry an empty Topic and only
    /// their coarse source Layer (e.g. "Law"). Because re-ingestion dedupes on UrlHash, they would
    /// never be repaired by the normal pipeline. This walks existing items with an empty Topic and
    /// stamps the curated topic set of their ingestion source onto them, in one bulk update per
    /// source. It is idempotent: once an item has a Topic it is never matched again.
    /// </summary>
    /// <summary>
    /// Re-enriches items that carry no "why it matters", newest and most media-first.
    /// </summary>
    /// <remarks>
    /// Everything ingested while the OpenRouter key was dead got the deterministic fallback: a
    /// truncated description for `whatHappened` and nothing at all for `whyItMatters`. Measured on
    /// staging, that was every video in the feed.
    ///
    /// Capped per cycle. Each item is a model call against a monthly spend cap, and a few hundred
    /// stale items must not spend a month's budget in one pass — they will be picked up on the
    /// next cycle instead.
    /// </remarks>
    private async Task BackfillEnrichmentAsync(CancellationToken ct)
    {
        // Twenty was over-careful. Measured on staging: 4,222 enrichment calls cost $0.92, about
        // two hundredths of a cent each, so draining several hundred stale items costs about ten
        // cents — against a cap the enricher already checks before every call. At twenty per
        // six-hour cycle the backlog would have taken the better part of a week to clear.
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var perCycle = scope.ServiceProvider
                .GetRequiredService<IConfiguration>()
                .GetValue("Ingestion:EnrichmentBackfillPerCycle", 120);
            var db = scope.ServiceProvider.GetRequiredService<RadarDatabase>();
            var enricher = scope.ServiceProvider.GetRequiredService<ContentEnricherService>();

            var missing = Builders<ContentItem>.Filter.Or(
                Builders<ContentItem>.Filter.Eq(c => c.WhyItMatters, string.Empty),
                Builders<ContentItem>.Filter.Eq(c => c.WhyItMatters, null));

            // Video and podcast first: they are the ones a learner is asked to spend twenty
            // minutes on, so an unexplained one costs more than an unexplained headline.
            var media = Builders<ContentItem>.Filter.In(c => c.Type, new[] { ContentType.Video, ContentType.Podcast });

            var pending = await db.ContentItems
                .Find(Builders<ContentItem>.Filter.And(missing, media))
                .SortByDescending(c => c.PublishedAt)
                .Limit(perCycle)
                .ToListAsync(ct);

            if (pending.Count < perCycle)
            {
                var rest = await db.ContentItems
                    .Find(missing)
                    .SortByDescending(c => c.PublishedAt)
                    .Limit(perCycle - pending.Count)
                    .ToListAsync(ct);

                var seen = pending.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
                pending.AddRange(rest.Where(r => !seen.Contains(r.Id)));
            }

            if (pending.Count == 0) return;

            var filled = 0;
            foreach (var item in pending)
            {
                if (ct.IsCancellationRequested) break;
                if (!await enricher.ReEnrichAsync(item, ct)) continue;

                await db.ContentItems.ReplaceOneAsync(c => c.Id == item.Id, item, cancellationToken: ct);
                filled++;
            }

            _log.LogInformation(
                "Enrichment backfill: {Filled} of {Considered} items now say why they matter.",
                filled, pending.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Enrichment backfill failed; continuing.");
        }
    }

    private async Task BackfillTopicsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadarDatabase>();

            var sourcesById = SourceRegistry.All
                .Where(s => s.Topics.Count > 0)
                .ToDictionary(s => s.Id, s => s.Topics, StringComparer.OrdinalIgnoreCase);
            if (sourcesById.Count == 0) return;

            var untagged = Builders<ContentItem>.Filter.Or(
                Builders<ContentItem>.Filter.Eq(c => c.Topic, string.Empty),
                Builders<ContentItem>.Filter.Eq(c => c.Topic, null));

            var pending = await db.ContentItems
                .Find(untagged)
                .Project(c => new { c.IngestionSourceId })
                .ToListAsync(ct);

            var sourceIds = pending
                .Select(p => p.IngestionSourceId)
                .OfType<string>()
                .Where(id => sourcesById.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sourceIds.Count == 0) return;

            long updated = 0;
            foreach (var sourceId in sourceIds)
            {
                var topics = sourcesById[sourceId];
                var filter = Builders<ContentItem>.Filter.And(
                    untagged,
                    Builders<ContentItem>.Filter.Eq(c => c.IngestionSourceId, sourceId));
                var update = Builders<ContentItem>.Update
                    .Set(c => c.Topic, topics[0])
                    .Set(c => c.SecondaryTopics, topics.Skip(1).ToList());

                var result = await db.ContentItems.UpdateManyAsync(filter, update, cancellationToken: ct);
                updated += result.ModifiedCount;
            }

            if (updated > 0)
                _log.LogInformation("Topic backfill: stamped curated topics onto {Count} item(s) across {Sources} source(s).", updated, sourceIds.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning("Topic backfill failed (non-fatal): {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Re-derives each item's layer from its own text, so existing items that inherited a general
    /// outlet's layer (e.g. every Premium Times article shown as "Law") are corrected without
    /// waiting for re-ingestion. Idempotent: items already matching their detected layer are skipped.
    /// </summary>
    private async Task ReconcileLayersAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadarDatabase>();

            var items = await db.ContentItems
                .Find(FilterDefinition<ContentItem>.Empty)
                .Project(c => new { c.Id, c.Layer, c.Title, c.Signal, c.AiSummary, c.WhatHappened })
                .Limit(5000)
                .ToListAsync(ct);

            var updatesByLayer = new Dictionary<ContentLayer, List<string>>();
            foreach (var item in items)
            {
                var detected = ContentClassifier.DetectLayer($"{item.Title} {item.Signal} {item.AiSummary} {item.WhatHappened}");
                if (detected is null || detected == item.Layer) continue;
                if (!updatesByLayer.TryGetValue(detected.Value, out var ids))
                    updatesByLayer[detected.Value] = ids = [];
                ids.Add(item.Id);
            }

            long updated = 0;
            foreach (var (layer, ids) in updatesByLayer)
            {
                var filter = Builders<ContentItem>.Filter.In(c => c.Id, ids);
                var update = Builders<ContentItem>.Update.Set(c => c.Layer, layer);
                updated += (await db.ContentItems.UpdateManyAsync(filter, update, cancellationToken: ct)).ModifiedCount;
            }

            if (updated > 0)
                _log.LogInformation("Layer reconcile: re-labelled {Count} item(s) from their content.", updated);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning("Layer reconcile failed (non-fatal): {Message}", ex.Message);
        }
    }

    private async Task RunCycleCoreAsync(CancellationToken ct)
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
            var youtube       = sp.GetRequiredService<YouTubeIngestionService>();
            var opportunities = sp.GetRequiredService<OpportunityIngestionService>();

            // Opportunities are stored in their own collection rather than as ContentItems, so they
            // do not go through RunFetchBatchAsync. Run first: they are the scarcest supply and the
            // daily batch job downstream depends on them existing.
            try
            {
                await opportunities.RunAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Opportunity ingestion failed; continuing with content sources.");
            }

            // YouTube channels (public Atom feeds; first run resolves and caches channel ids).
            var youtubeItems = await youtube.FetchAllAsync(ct);
            totalNew += await RunFetchBatchAsync(db, enricher, youtubeItems, ct);

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

        await BackfillEnrichmentAsync(ct);

        _log.LogInformation("Ingestion cycle complete. {Total} new items added.", totalNew);
    }

    // ── RSS batch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes what each source just did, so "is this source working?" is a question with an answer.
    /// </summary>
    /// <remarks>
    /// Upserted per source and kept indefinitely — it is one small document per source, and the
    /// value is in being able to see that a feed stopped producing three weeks ago.
    /// </remarks>
    private async Task RecordSourceHealthAsync(
        Dictionary<string, RssIngestionService.FetchOutcome> outcomes,
        CancellationToken ct)
    {
        if (outcomes.Count == 0) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadarDatabase>();
            var now = DateTime.UtcNow;

            var writes = new List<WriteModel<SourceHealthDoc>>();

            foreach (var source in SourceRegistry.All)
            {
                if (!outcomes.TryGetValue(source.Id, out var outcome)) continue;

                var produced = outcome.Items.Count > 0;

                var update = Builders<SourceHealthDoc>.Update
                    .Set(h => h.Name, source.Name)
                    .Set(h => h.Layer, source.Layer.ToString())
                    .Set(h => h.LastAttemptAt, now)
                    .Set(h => h.LastHttpStatus, outcome.HttpStatus)
                    .Set(h => h.LastItemsFetched, outcome.Items.Count)
                    .Set(h => h.LastError, outcome.Error);

                // A run of empties is what distinguishes a quiet publisher from a dead feed.
                update = produced
                    ? update.Set(h => h.LastSuccessAt, now).Set(h => h.ConsecutiveEmpty, 0)
                    : update.Inc(h => h.ConsecutiveEmpty, 1);

                writes.Add(new UpdateOneModel<SourceHealthDoc>(
                    Builders<SourceHealthDoc>.Filter.Eq(h => h.Id, source.Id),
                    update)
                { IsUpsert = true });
            }

            if (writes.Count > 0) await db.SourceHealth.BulkWriteAsync(writes, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Recording health must never be the reason ingestion fails.
            _log.LogWarning(ex, "Could not record source health; ingestion continues.");
        }
    }

    private async Task<List<RawFeedItem>> FetchRssAllSourcesAsync(RssIngestionService rss, CancellationToken ct)
    {
        // Fetched eight at a time. This is pure HTTP against 258 different hosts — nobody is
        // rate-limited by it — and sequentially at a second or two each it took the better part of
        // ten minutes before the end of the registry was reached at all. Newly added sources live
        // at that end, which is a large part of why twenty of them produced nothing for an hour.
        var sources = SourceRegistry.All.Where(s => s.IsEnabled).ToList();
        var all = new List<RawFeedItem>();
        var health = new Dictionary<string, RssIngestionService.FetchOutcome>(StringComparer.Ordinal);
        var sync = new object();

        using var gate = new SemaphoreSlim(8);

        await Task.WhenAll(sources.Select(async source =>
        {
            if (ct.IsCancellationRequested) return;
            await gate.WaitAsync(ct);
            try
            {
                var outcome = await rss.FetchAsync(source, ct);
                lock (sync)
                {
                    all.AddRange(outcome.Items);
                    health[source.Id] = outcome;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogWarning("RSS fetch failed for {Source}: {Message}", source.Name, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }));

        await RecordSourceHealthAsync(health, ct);
        return all;
    }

    // ── Generic enrich + persist batch ───────────────────────────────────────

    private async Task<int> RunFetchBatchAsync(
        RadarDatabase db, ContentEnricherService enricher,
        List<RawFeedItem> rawItems, CancellationToken ct)
    {
        var newItems = await FilterNewItemsAsync(db, rawItems, ct);

        // Round-robin across sources, not source by source.
        //
        // Taking five from each source in turn put every item from the last source behind every
        // item from all the others, so a cycle cut short — by a deploy, a restart, a
        // cancellation — covered the start of the registry and never reached the end. Twenty
        // sources appended to the registry produced nothing for an hour for exactly this reason.
        //
        // Interleaved, every source gets its first item before any source gets its second, so a
        // partial cycle is a thin slice of everything rather than all of a few.
        var bySource = newItems
            .GroupBy(i => i.SourceId)
            .Select(g => g.Take(MaxEnrichPerSource).ToList())
            .ToList();

        var toEnrich = new List<RawFeedItem>();
        for (var round = 0; round < MaxEnrichPerSource; round++)
            foreach (var group in bySource)
                if (round < group.Count)
                    toEnrich.Add(group[round]);

        // Enriched a few at a time rather than one after another with a two-second sleep between.
        //
        // That sleep, plus the model call, cost about five seconds an item. At five items from
        // each of 258 sources it put a cycle at roughly an hour — and sources added to the end of
        // the registry sit at the end of that queue, which is why twenty newly added feeds showed
        // nothing an hour after they were configured. The delay was presumably there to be polite
        // to the provider; four at a time with a short pause is still polite, and finishes.
        using var gate = new SemaphoreSlim(EnrichConcurrency);
        var count = 0;

        var work = toEnrich.Select(async raw =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var item = await enricher.EnrichAsync(raw, ct);
                await db.ContentItems.InsertOneAsync(item, cancellationToken: ct);
                Interlocked.Increment(ref count);
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogWarning("Enrichment/persist failed for '{Title}': {Message}", raw.Title, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(work);
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

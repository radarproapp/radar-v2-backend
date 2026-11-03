using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What is actually happening in Radar, read from the events it already records.
/// </summary>
/// <remarks>
/// Eighteen kinds of event were being written to <c>analytics_events</c> - impressions, opens,
/// saves, dismissals, reports, searches, applications - each with a user, a content id, a feed
/// position and a time. Two things read them: the daily digest, so it does not resend something
/// you have seen, and the user graph. Nothing in the admin area touched them at all.
///
/// So the data for "what are people engaging with" has been in the database the whole time and
/// has never been looked at. This reads it.
///
/// Everything here is counting. No model is called, nothing is inferred, and a question the
/// events cannot answer gets an empty result rather than an estimate - a dashboard that
/// estimates is worse than one that says it does not know, because a number on a screen gets
/// quoted in a meeting.
/// </remarks>
public class AdminInsightsService
{
    private readonly RadarDatabase _db;

    public AdminInsightsService(RadarDatabase db) => _db = db;

    /// <summary>How far back a dashboard question looks, bounded so a query cannot run away.</summary>
    private static DateTime Since(int days) => DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 180));

    // ── activity ─────────────────────────────────────────────────────────────

    public sealed record DayOfActivity(DateTime Day, long Events, long People);

    /// <summary>
    /// Events and distinct people per day.
    /// </summary>
    /// <remarks>
    /// People, not sessions. One person opening forty things is one engaged reader and forty
    /// events, and a chart that only plots events cannot tell that from forty readers - which
    /// is the difference between a product working and one person refreshing.
    /// </remarks>
    public async Task<List<DayOfActivity>> ActivityByDayAsync(int days = 30, CancellationToken ct = default)
    {
        var since = Since(days);

        var rows = await _db.AnalyticsEvents.Aggregate()
            .Match(e => e.CreatedAt >= since)
            .Group(new BsonDocument
            {
                ["_id"] = new BsonDocument("$dateToString", new BsonDocument
                {
                    ["format"] = "%Y-%m-%d",
                    ["date"] = "$CreatedAt",
                }),
                ["events"] = new BsonDocument("$sum", 1),
                ["people"] = new BsonDocument("$addToSet", "$UserId"),
            })
            .ToListAsync(ct);

        return [.. rows
            .Select(row => new DayOfActivity(
                DateTime.Parse(row["_id"].AsString, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
                row["events"].ToInt64(),
                row["people"].AsBsonArray.Count))
            .OrderBy(day => day.Day)];
    }

    /// <summary>How many of each kind of thing happened, over the window.</summary>
    public async Task<Dictionary<string, long>> EventMixAsync(int days = 30, CancellationToken ct = default)
    {
        var since = Since(days);

        var rows = await _db.AnalyticsEvents.Aggregate()
            .Match(e => e.CreatedAt >= since)
            .Group(new BsonDocument
            {
                ["_id"] = "$Type",
                ["count"] = new BsonDocument("$sum", 1),
            })
            .ToListAsync(ct);

        // Type persists as an Int32, so the stored value is the enum's position. Named here
        // rather than on the screen, because a dashboard reading "7" helps nobody.
        return rows
            .Where(row => row["_id"].IsInt32 && Enum.IsDefined(typeof(AnalyticsEventType), row["_id"].AsInt32))
            .ToDictionary(
                row => ((AnalyticsEventType)row["_id"].AsInt32).ToString(),
                row => row["count"].ToInt64());
    }

    // ── the funnel ───────────────────────────────────────────────────────────

    public sealed record Funnel(long Impressions, long Opens, long Saves, long Dismissals, long Reports);

    /// <summary>
    /// Section 23's question: are people finding useful information, or only scrolling?
    /// </summary>
    /// <remarks>
    /// Counted from stored events rather than derived from each other, so a stage can be larger
    /// than the one above it and the numbers will say so. That is not an error to hide - it
    /// means something is being saved without an impression ever being recorded, and the
    /// honest reading is that the impression logging has a gap.
    /// </remarks>
    public async Task<Funnel> FunnelAsync(int days = 30, CancellationToken ct = default)
    {
        var since = Since(days);

        async Task<long> Count(AnalyticsEventType type) =>
            await _db.AnalyticsEvents.CountDocumentsAsync(
                e => e.CreatedAt >= since && e.Type == type, cancellationToken: ct);

        return new Funnel(
            await Count(AnalyticsEventType.Impression),
            await Count(AnalyticsEventType.Open),
            await Count(AnalyticsEventType.Save),
            await Count(AnalyticsEventType.NotRelevant),
            await Count(AnalyticsEventType.Reported));
    }

    // ── what they engage with ────────────────────────────────────────────────

    public sealed record EngagedItem(string ContentItemId, string Title, string Source, string Type, long Opens, long Saves);

    /// <summary>
    /// The content people actually opened, most first.
    /// </summary>
    /// <remarks>
    /// Joined back to the item so a row says what the thing was. An id is not a finding: the
    /// question behind this screen is "what are they engaging with", and the answer has to be
    /// readable without a second query.
    ///
    /// Items that have since been deleted are dropped rather than shown as a blank row, because
    /// a blank row looks like a bug in the dashboard rather than a deletion in the corpus.
    /// </remarks>
    public async Task<List<EngagedItem>> MostEngagedAsync(int days = 30, int limit = 20, CancellationToken ct = default)
    {
        var since = Since(days);
        limit = Math.Clamp(limit, 1, 100);

        var rows = await _db.AnalyticsEvents.Aggregate()
            .Match(e => e.CreatedAt >= since
                        && e.ContentItemId != null
                        && (e.Type == AnalyticsEventType.Open || e.Type == AnalyticsEventType.Save))
            .Group(new BsonDocument
            {
                ["_id"] = "$ContentItemId",
                ["opens"] = new BsonDocument("$sum", new BsonDocument(
                    "$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Type", (int)AnalyticsEventType.Open }),
                        1, 0,
                    })),
                ["saves"] = new BsonDocument("$sum", new BsonDocument(
                    "$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Type", (int)AnalyticsEventType.Save }),
                        1, 0,
                    })),
            })
            .Sort(new BsonDocument { ["opens"] = -1, ["saves"] = -1 })
            .Limit(limit)
            .ToListAsync(ct);

        var ids = rows.Select(row => row["_id"].AsString).ToList();
        if (ids.Count == 0) return [];

        var items = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.In(item => item.Id, ids))
            .ToListAsync(ct);

        var byId = items.ToDictionary(item => item.Id);

        return [.. rows
            .Where(row => byId.ContainsKey(row["_id"].AsString))
            .Select(row =>
            {
                var item = byId[row["_id"].AsString];
                return new EngagedItem(
                    item.Id, item.Title, item.Source, item.Type.ToString(),
                    row["opens"].ToInt64(), row["saves"].ToInt64());
            })];
    }

    public sealed record SourceEngagement(string Source, long Published, long Opens, long Dismissals);

    /// <summary>
    /// Which sources earn attention, as opposed to which produce volume.
    /// </summary>
    /// <remarks>
    /// Published and opened side by side on purpose. A source filing forty items a day and
    /// earning three opens is costing ingestion budget and feed space to no one's benefit, and
    /// that is invisible on any chart that ranks by opens alone.
    ///
    /// "Published" counts by the date on the item, because ContentItem records no ingestion
    /// time of its own. A source that back-dates what it files will under-report here, so the
    /// column is what the sources claim rather than what Radar took in.
    /// </remarks>
    public async Task<List<SourceEngagement>> BySourceAsync(int days = 30, int limit = 25, CancellationToken ct = default)
    {
        var since = Since(days);
        limit = Math.Clamp(limit, 1, 100);

        var engaged = await _db.AnalyticsEvents.Aggregate()
            .Match(e => e.CreatedAt >= since && e.ContentItemId != null
                        && (e.Type == AnalyticsEventType.Open || e.Type == AnalyticsEventType.NotRelevant))
            .ToListAsync(ct);

        if (engaged.Count == 0) return [];

        var ids = engaged.Select(e => e.ContentItemId!).Distinct().ToList();

        var items = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.In(item => item.Id, ids))
            .Project(item => new { item.Id, item.Source })
            .ToListAsync(ct);

        var sourceOf = items.ToDictionary(item => item.Id, item => item.Source);

        var published = await _db.ContentItems.Aggregate()
            .Match(item => item.PublishedAt >= since)
            .Group(new BsonDocument { ["_id"] = "$Source", ["count"] = new BsonDocument("$sum", 1) })
            .ToListAsync(ct);

        var publishedBySource = published
            .Where(row => row["_id"].IsString)
            .ToDictionary(row => row["_id"].AsString, row => row["count"].ToInt64());

        return [.. engaged
            .Where(e => sourceOf.ContainsKey(e.ContentItemId!))
            .GroupBy(e => sourceOf[e.ContentItemId!])
            .Select(group => new SourceEngagement(
                group.Key,
                publishedBySource.GetValueOrDefault(group.Key),
                group.Count(e => e.Type == AnalyticsEventType.Open),
                group.Count(e => e.Type == AnalyticsEventType.NotRelevant)))
            .OrderByDescending(source => source.Opens)
            .Take(limit)];
    }
}

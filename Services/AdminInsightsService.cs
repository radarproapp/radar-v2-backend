using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What is actually happening in Radar, read from the events it already records.
/// </summary>
/// <remarks>
/// Seventeen kinds of event are written to <c>analytics_events</c> - impressions, opens, saves,
/// dismissals, reports, searches, applications - each with a user, a content id, a feed position
/// and a time. Two things read them: the daily digest, so it does not resend something you have
/// seen, and the user graph. Nothing in the admin area touched them at all.
///
/// So the data for "what are people engaging with" has been in the database the whole time and
/// has never been looked at. This reads it.
///
/// Everything here is counting. No model is called, nothing is inferred, and a question the
/// events cannot answer gets an empty result rather than an estimate - a dashboard that
/// estimates is worse than one that says it does not know, because a number on a screen gets
/// quoted in a meeting.
///
/// Counted in the database rather than in this process. Every method here runs over a collection
/// that grows with every scroll of every feed; pulling a window of it into managed memory to run
/// LINQ over costs a second and sixty megabytes at sixty thousand events, and both figures are
/// linear. An admin page is not worth an outage.
/// </remarks>
public class AdminInsightsService
{
    private readonly RadarDatabase _db;

    public AdminInsightsService(RadarDatabase db) => _db = db;

    /// <summary>The widest window a dashboard question may ask for.</summary>
    private const int LongestWindow = 180;

    /// <summary>
    /// The window actually used, which is not always the one that was asked for.
    /// </summary>
    /// <remarks>
    /// Public so a caller can report what it did. Clamping quietly and then echoing the request
    /// back puts "nothing recorded in the last 10000 days" above 180 days of data, which is a
    /// sentence the dashboard made up.
    /// </remarks>
    public static int WindowFor(int days) => Math.Clamp(days, 1, LongestWindow);

    private static DateTime Since(int days) => DateTime.UtcNow.AddDays(-WindowFor(days));

    // ── activity ─────────────────────────────────────────────────────────────

    public sealed record DayOfActivity(DateTime Day, long Events, long People);

    /// <summary>
    /// Events and distinct people per day.
    /// </summary>
    /// <remarks>
    /// People, not sessions. One person opening forty things is one engaged reader and forty
    /// events, and a chart that only plots events cannot tell that from forty readers - which
    /// is the difference between a product working and one person refreshing.
    ///
    /// Counted by grouping twice rather than by collecting the ids and measuring the array. The
    /// array version sends every distinct user id for every day to this process to be counted -
    /// a megabyte and a half at a thousand users - and fails the whole aggregation outright once
    /// one day's array passes sixteen megabytes. Two groups return one integer per day.
    /// </remarks>
    public async Task<List<DayOfActivity>> ActivityByDayAsync(int days = 30, CancellationToken ct = default)
    {
        var since = Since(days);

        var rows = await _db.AnalyticsEvents.Aggregate()
            .Match(e => e.CreatedAt >= since)
            .Group(new BsonDocument
            {
                ["_id"] = new BsonDocument
                {
                    ["day"] = new BsonDocument("$dateToString", new BsonDocument
                    {
                        ["format"] = "%Y-%m-%d",
                        ["date"] = "$CreatedAt",
                    }),
                    ["person"] = "$UserId",
                },
                ["events"] = new BsonDocument("$sum", 1),
            })
            .Group(new BsonDocument
            {
                ["_id"] = "$_id.day",
                ["events"] = new BsonDocument("$sum", "$events"),
                ["people"] = new BsonDocument("$sum", 1),
            })
            .ToListAsync(ct);

        return [.. rows
            .Select(row => new DayOfActivity(
                // ParseExact against the invariant culture, because the format is this method's
                // own and not the host's. Parse with CurrentCulture reads 2026-11-03 as 1483 in
                // a Buddhist calendar and throws outright in ar-SA, which is one environment
                // variable away from a 500 on this endpoint.
                DateTime.ParseExact(row["_id"].AsString, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                row["events"].ToInt64(),
                row["people"].ToInt64()))
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
    /// Feed content only - every stage requires a content id. Opens are also recorded for
    /// opportunities, which carry an opportunity id and no content id, and on an ordinary week
    /// those outnumber feed impressions. Counting them here made Opens exceed Impressions by
    /// design, under a caption telling the reader that impression logging has a gap - which
    /// sends an engineer hunting a bug that does not exist. Those opens are not lost:
    /// <see cref="EventMixAsync"/> counts every event of every kind.
    ///
    /// Counted from stored events rather than derived from each other, so a stage can still come
    /// out larger than the one above it and the numbers will say so. Now that the five stages
    /// share one population, that really does mean a logging gap.
    /// </remarks>
    public async Task<Funnel> FunnelAsync(int days = 30, CancellationToken ct = default)
    {
        var since = Since(days);

        async Task<long> Count(AnalyticsEventType type) =>
            await _db.AnalyticsEvents.CountDocumentsAsync(
                e => e.CreatedAt >= since && e.Type == type && e.ContentItemId != null,
                cancellationToken: ct);

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
    /// The join happens before the limit, which is the whole reason it runs in the pipeline.
    /// Limiting first and joining afterwards deletes a row for every id whose item has since
    /// been purged - and ingestion purges duplicate urls on every cycle, so dangling ids are
    /// routine rather than hypothetical. Three deleted-but-popular items were enough to return
    /// an empty list under the caption "nothing has been opened", with thirty opens sitting
    /// inside the window.
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
                ["opens"] = CountOf(AnalyticsEventType.Open),
                ["saves"] = CountOf(AnalyticsEventType.Save),
            })
            .AppendStage<BsonDocument>(LookUpTheItem)
            // An item that has since been deleted is dropped rather than shown as a blank row,
            // because a blank row looks like a bug in the dashboard rather than a deletion in
            // the corpus. Dropped here, before the limit, so it costs a row of the corpus and
            // not a row of the answer.
            .AppendStage<BsonDocument>(new BsonDocument("$match",
                new BsonDocument("item.0", new BsonDocument("$exists", true))))
            .AppendStage<BsonDocument>(new BsonDocument("$sort", new BsonDocument { ["opens"] = -1, ["saves"] = -1 }))
            .AppendStage<BsonDocument>(new BsonDocument("$limit", limit))
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                ["opens"] = 1,
                ["saves"] = 1,
                ["title"] = FirstOf("$item.Title"),
                ["source"] = FirstOf("$item.Source"),
                ["type"] = FirstOf("$item.Type"),
            }))
            .ToListAsync(ct);

        return [.. rows.Select(row => new EngagedItem(
            row["_id"].AsString,
            Text(row, "title", "Untitled"),
            Text(row, "source", "Unknown"),
            NameOfContentType(row.GetValue("type", BsonNull.Value)),
            row["opens"].ToInt64(),
            row["saves"].ToInt64()))];
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
    /// Which is what this used to be. Rows were built out of the engaged events, so a source
    /// with twelve hundred published items and no engagement at all never appeared - the one
    /// case the paragraph above says the screen exists to expose. Rows now come from both
    /// sides, and the ranking is the larger of a source's volume and its attention, so neither
    /// a publisher nobody reads nor a source read constantly without filing anything new can
    /// fall off the end.
    ///
    /// "Published" counts by the date on the item, because ContentItem records no ingestion
    /// time of its own. A source that back-dates what it files will under-report here, so the
    /// column is what the sources claim rather than what Radar took in.
    /// </remarks>
    public async Task<List<SourceEngagement>> BySourceAsync(int days = 30, int limit = 25, CancellationToken ct = default)
    {
        var since = Since(days);
        limit = Math.Clamp(limit, 1, 100);

        // Grouped down to a source inside the pipeline. Reading the matching events into this
        // process to group them in LINQ cost 1.2 seconds and 64 MB of allocation at sixty
        // thousand events, both linear in the collection, and the id list it then built would
        // eventually put the follow-up query past MongoDB's 16 MB command limit.
        var engaged = await _db.AnalyticsEvents.Aggregate()
            .Match(e => e.CreatedAt >= since && e.ContentItemId != null
                        && (e.Type == AnalyticsEventType.Open || e.Type == AnalyticsEventType.NotRelevant))
            .Group(new BsonDocument
            {
                ["_id"] = "$ContentItemId",
                ["opens"] = CountOf(AnalyticsEventType.Open),
                ["dismissals"] = CountOf(AnalyticsEventType.NotRelevant),
            })
            .AppendStage<BsonDocument>(LookUpTheItem)
            .AppendStage<BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = FirstOf("$item.Source"),
                ["opens"] = new BsonDocument("$sum", "$opens"),
                ["dismissals"] = new BsonDocument("$sum", "$dismissals"),
            }))
            // Events whose item is gone group under a missing source. Dropped rather than shown
            // as an unnamed row, for the same reason a deleted item is dropped above.
            .AppendStage<BsonDocument>(new BsonDocument("$match",
                new BsonDocument("_id", new BsonDocument("$type", "string"))))
            .ToListAsync(ct);

        var engagedBySource = engaged.ToDictionary(
            row => row["_id"].AsString,
            row => (Opens: row["opens"].ToInt64(), Dismissals: row["dismissals"].ToInt64()));

        var published = await _db.ContentItems.Aggregate()
            .Match(item => item.PublishedAt >= since)
            .Group(new BsonDocument { ["_id"] = "$Source", ["count"] = new BsonDocument("$sum", 1) })
            .ToListAsync(ct);

        var publishedBySource = published
            .Where(row => row["_id"].IsString)
            .ToDictionary(row => row["_id"].AsString, row => row["count"].ToInt64());

        return [.. publishedBySource.Keys
            .Union(engagedBySource.Keys, StringComparer.Ordinal)
            .Select(source =>
            {
                var engagement = engagedBySource.GetValueOrDefault(source);
                return new SourceEngagement(
                    source,
                    publishedBySource.GetValueOrDefault(source),
                    engagement.Opens,
                    engagement.Dismissals);
            })
            .OrderByDescending(source => Math.Max(source.Published, source.Opens))
            .ThenByDescending(source => source.Opens)
            .ThenBy(source => source.Source, StringComparer.OrdinalIgnoreCase)
            .Take(limit)];
    }

    // ── shared pipeline pieces ───────────────────────────────────────────────

    /// <summary>Counts one kind of event inside a group that holds several kinds.</summary>
    private static BsonDocument CountOf(AnalyticsEventType type) =>
        new("$sum", new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$Type", (int)type }),
            1, 0,
        }));

    /// <summary>
    /// Joins a group keyed on a content id back to the item itself.
    /// </summary>
    /// <remarks>
    /// The collection name is written out, because this runs as a raw stage rather than through
    /// the typed builder. It has to match <see cref="RadarDatabase.ContentItems"/>.
    /// </remarks>
    private static BsonDocument LookUpTheItem => new("$lookup", new BsonDocument
    {
        ["from"] = "content_items",
        ["localField"] = "_id",
        ["foreignField"] = "_id",
        ["as"] = "item",
    });

    /// <summary>A field off the joined item, which $lookup always hands back as an array.</summary>
    private static BsonDocument FirstOf(string path) =>
        new("$arrayElemAt", new BsonArray { path, 0 });

    /// <summary>A string field, or a word, rather than whatever a missing field renders as.</summary>
    private static string Text(BsonDocument row, string field, string whenMissing) =>
        row.GetValue(field, BsonNull.Value) is BsonString value && value.Value.Length > 0
            ? value.Value
            : whenMissing;

    /// <summary>
    /// A content type's name, or "Unknown" when the stored value is not one Radar knows.
    /// </summary>
    /// <remarks>
    /// A value outside the enum degrades to a word rather than to a number nobody can read, or
    /// to an exception that takes the whole dashboard down with it.
    /// </remarks>
    private static string NameOfContentType(BsonValue stored) =>
        stored.IsInt32 && Enum.IsDefined(typeof(ContentType), stored.AsInt32)
            ? ((ContentType)stored.AsInt32).ToString()
            : stored.IsString ? stored.AsString : "Unknown";
}

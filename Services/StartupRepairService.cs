using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// One-off repairs to data that an earlier version of the code wrote wrongly.
/// </summary>
/// <remarks>
/// These lived inside <c>RadarDatabase.EnsureIndexes</c>, which runs on a fire-and-forget
/// <c>Task.Run</c> wrapped in a bare <c>catch { }</c>. So an exception anywhere in that long
/// method silently abandons everything after it, and there is no way to find out whether a repair
/// ran — which is exactly what happened: six invented opportunities were still being served after
/// a deploy that was supposed to delete them, and I had no signal either way.
///
/// Here instead, because a repair needs three things that method cannot give it: a log line per
/// step, failures that are reported rather than swallowed, and a result observable from outside
/// the box. <see cref="LastRun"/> is surfaced on <c>/api/version</c> so "did the cleanup run?" is
/// a question with an answer.
///
/// Every repair is idempotent and cheap once the data is clean. They stay rather than becoming a
/// migration nobody remembers to run.
/// </remarks>
public class StartupRepairService : BackgroundService
{
    private readonly RadarDatabase _db;
    private readonly ILogger<StartupRepairService> _log;

    public StartupRepairService(RadarDatabase db, ILogger<StartupRepairService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>What the last run did, for <c>/api/version</c>.</summary>
    public static IReadOnlyDictionary<string, string> LastRun { get; private set; } =
        new Dictionary<string, string> { ["state"] = "not started" };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var results = new Dictionary<string, string>();

        await RunAsync(results, "invented-opportunities", RemoveInventedOpportunitiesAsync, ct);
        await RunAsync(results, "objectid-playback-rows", RemoveObjectIdPlaybackRowsAsync, ct);

        results["state"] = "complete";
        results["ranAt"] = DateTime.UtcNow.ToString("O");
        LastRun = results;
    }

    /// <summary>
    /// Runs one repair, recording what it did or why it could not.
    /// </summary>
    /// <remarks>
    /// Each is isolated: one failing repair must not abandon the rest, which is the specific way
    /// the old arrangement lost them.
    /// </remarks>
    private async Task RunAsync(
        Dictionary<string, string> results,
        string name,
        Func<CancellationToken, Task<long>> repair,
        CancellationToken ct)
    {
        try
        {
            var removed = await repair(ct);
            results[name] = $"removed {removed}";

            if (removed > 0)
                _log.LogWarning("Startup repair {Repair} removed {Count} rows.", name, removed);
            else
                _log.LogInformation("Startup repair {Repair}: nothing to do.", name);
        }
        catch (OperationCanceledException)
        {
            results[name] = "cancelled";
        }
        catch (Exception ex)
        {
            results[name] = $"failed: {ex.GetType().Name}";
            _log.LogError(ex, "Startup repair {Repair} failed.", name);
        }
    }

    /// <summary>
    /// Deletes the sixteen hand-written opportunities, and anything pointing at them.
    /// </summary>
    /// <remarks>
    /// Five were programmes that do not exist, attributed to Google, DeepMind and HackerOne;
    /// three were job openings invented at Paystack, Cloudflare and Stripe. Deleting them from
    /// the source stopped new ones being written and left the existing rows on the page, with
    /// deadlines frozen from <c>DateTime.UtcNow.AddDays(n)</c>.
    ///
    /// Matched on the id prefix the seed used. Ingested opportunities carry a GUID, so the prefix
    /// cannot collide with a real one.
    /// </remarks>
    private async Task<long> RemoveInventedOpportunitiesAsync(CancellationToken ct)
    {
        var invented = Builders<Models.Opportunity>.Filter.Regex(
            o => o.Id, new BsonRegularExpression("^opp-"));

        var ids = await _db.Opportunities.Find(invented).Project(o => o.Id).ToListAsync(ct);
        if (ids.Count == 0) return 0;

        var result = await _db.Opportunities.DeleteManyAsync(invented, ct);

        // A save or an application pointing at a fabrication should go with it, or the user keeps
        // a tracked application to something that was never offered.
        await _db.SavedOpportunities.DeleteManyAsync(s => ids.Contains(s.OpportunityId), ct);
        await _db.AppliedOpportunities.DeleteManyAsync(a => ids.Contains(a.OpportunityId), ct);
        await _db.OpportunityTrackings.DeleteManyAsync(t => ids.Contains(t.OpportunityId), ct);

        return result.DeletedCount;
    }

    /// <summary>
    /// Deletes playback rows whose <c>_id</c> is an ObjectId.
    /// </summary>
    /// <remarks>
    /// The first version of the heartbeat upsert filtered on (user, item) and set no id, so Mongo
    /// generated an ObjectId that the string Id property cannot deserialize. The write succeeded
    /// and the read failed, so those rows are unreadable, are matched as updates rather than
    /// inserts — meaning the fix never applies to them — and break the graph, which reads the
    /// whole collection.
    ///
    /// A delete does not deserialize what it removes, which is the only reason this is possible.
    /// </remarks>
    private async Task<long> RemoveObjectIdPlaybackRowsAsync(CancellationToken ct)
    {
        var filter = new BsonDocument("_id", new BsonDocument("$type", "objectId"));

        var result = await _db.PlaybackProgress
            .WithRemappedType()
            .DeleteManyAsync(filter, ct);

        return result.DeletedCount;
    }
}

internal static class PlaybackCollectionExtensions
{
    /// <summary>
    /// The same collection typed as raw BSON.
    /// </summary>
    /// <remarks>
    /// A typed collection will happily take a raw filter, but keeping the repair untyped makes it
    /// obvious that it must not touch the documents it matches — they are the ones that cannot be
    /// read.
    /// </remarks>
    internal static IMongoCollection<BsonDocument> WithRemappedType(
        this IMongoCollection<PlaybackProgressDoc> collection) =>
        collection.Database.GetCollection<BsonDocument>(collection.CollectionNamespace.CollectionName);
}

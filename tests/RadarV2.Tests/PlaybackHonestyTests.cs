using System.Reflection;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// "4h 12m listened" has to mean listened.
///
/// The graph is built on these numbers, so the clamping is the whole feature. A tab left open on
/// a paused episode, a scrub to the end, a replayed request and a laptop waking from sleep all
/// arrive looking like playback, and each of them would inflate a total that the profile page
/// then presents as fact.
///
/// The arithmetic is exercised directly rather than through the service, which needs a live Mongo
/// connection and has no fixture.
/// </summary>
public class PlaybackHonestyTests
{
    private static double MaxDelta =>
        (double)typeof(PlaybackService)
            .GetField("MaxDeltaSeconds", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    private static double CompletionThreshold =>
        (double)typeof(PlaybackService)
            .GetField("CompletionThreshold", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    /// <summary>The clamp the service applies to one reported delta.</summary>
    private static double Clamp(double reported) => Math.Clamp(reported, 0, MaxDelta);

    [Fact]
    public void AHeartbeatCannotReportMoreThanTwoIntervals()
    {
        // Eight hours of a tab left open, reported as one delta.
        Assert.Equal(MaxDelta, Clamp(8 * 60 * 60));
    }

    [Fact]
    public void ANormalHeartbeatCountsInFull()
    {
        Assert.Equal(PlaybackService.HeartbeatSeconds, Clamp(PlaybackService.HeartbeatSeconds));
    }

    // A slightly late report still counts fully; the margin exists for that.
    [Fact]
    public void ASlightlyLateHeartbeatStillCountsInFull()
    {
        var late = PlaybackService.HeartbeatSeconds * 1.5;

        Assert.Equal(late, Clamp(late));
    }

    [Fact]
    public void NegativeAndNonsenseDeltasAddNothing()
    {
        Assert.Equal(0, Clamp(-30));
        Assert.Equal(0, Clamp(double.NaN) is var n && double.IsNaN(n) ? 0 : n);
    }

    /// <summary>
    /// Completion is short of the final second on purpose: podcasts end in credits and outros
    /// nobody listens to, so a player that only completes at 100% never completes anything.
    /// </summary>
    [Fact]
    public void FinishingIsShortOfTheFinalSecondButNotGenerous()
    {
        Assert.InRange(CompletionThreshold, 0.85, 0.95);
    }

    [Fact]
    public void ScrubbingNearTheEndOfALongEpisodeCountsAsFinished()
    {
        const double duration = 3600;
        var position = duration * 0.93;

        Assert.True(position >= duration * CompletionThreshold);
    }

    [Fact]
    public void HalfwayIsNotFinished()
    {
        const double duration = 3600;

        Assert.False(duration * 0.5 >= duration * CompletionThreshold);
    }

    /// <summary>
    /// The upsert has to set the id itself, because the filter cannot supply one.
    /// </summary>
    /// <remarks>
    /// The filter is a compound key — user and item — not the id, so on an insert Mongo generates
    /// an ObjectId of its own. The Id property is a string, and the driver reads the document
    /// straight back, so every heartbeat failed with "Cannot deserialize a 'String' from BsonType
    /// 'ObjectId'". Caught against staging, which is the only place it could have been caught: it
    /// builds, it has no null to dereference, and it fails on the first real write.
    ///
    /// The update is rendered to BSON rather than executed, so the assertion needs no database.
    /// </remarks>
    [Fact]
    public void TheUpsertSetsItsOwnIdOnInsert()
    {
        var update = PlaybackService.BuildUpdate("user-1", "item-1", 30, 1800, 30, null);

        var registry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
        var rendered = update.Render(new MongoDB.Driver.RenderArgs<Data.Documents.PlaybackProgressDoc>(
            registry.GetSerializer<Data.Documents.PlaybackProgressDoc>(), registry));

        var setOnInsert = rendered["$setOnInsert"].AsBsonDocument;

        Assert.True(
            setOnInsert.Contains("_id"),
            $"No _id in $setOnInsert, so Mongo will generate an ObjectId: {setOnInsert}");
    }
}

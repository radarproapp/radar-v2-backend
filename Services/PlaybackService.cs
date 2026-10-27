using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// Records how far through a video or podcast somebody got, and how much of it they actually
/// played.
/// </summary>
/// <remarks>
/// The client sends a position and the seconds that elapsed since its last heartbeat. It does not
/// send a running total, and the server does not trust one: a tab that was open for an hour on a
/// paused episode would otherwise report an hour of listening, and so would a scrub to the end.
///
/// Each reported delta is clamped before it is added. Anything longer than the gap between
/// heartbeats plus a margin is a lie or a bug — a sleeping laptop, a replayed request, a clock
/// jump — and is capped rather than rejected, because the playing did happen even when the number
/// attached to it is wrong.
/// </remarks>
public class PlaybackService
{
    private readonly RadarDatabase _db;

    public PlaybackService(RadarDatabase db) => _db = db;

    /// <summary>How often the client reports in. The clamp is derived from this.</summary>
    public const double HeartbeatSeconds = 10;

    /// <summary>
    /// The most a single heartbeat may add. Twice the interval, so a late or slightly delayed
    /// report still counts in full while a half-hour gap does not.
    /// </summary>
    private const double MaxDeltaSeconds = HeartbeatSeconds * 2;

    /// <summary>
    /// Fraction of the runtime that counts as finished.
    /// </summary>
    /// <remarks>
    /// Not 100%: podcasts end in credits and outros that nobody listens to, and a player that
    /// only marks completion at the final second never marks it at all. 92% is past the content
    /// and short of pretending.
    /// </remarks>
    private const double CompletionThreshold = 0.92;

    public async Task<PlaybackProgressDoc?> GetAsync(string userId, string contentItemId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(contentItemId)) return null;

        return await _db.PlaybackProgress
            .Find(p => p.UserId == userId && p.ContentItemId == contentItemId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Resume points for a set of items, for a list that wants to show them all.</summary>
    public async Task<Dictionary<string, PlaybackProgressDoc>> GetManyAsync(
        string userId, IReadOnlyCollection<string> contentItemIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || contentItemIds.Count == 0) return [];

        var rows = await _db.PlaybackProgress
            .Find(p => p.UserId == userId && contentItemIds.Contains(p.ContentItemId))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ContentItemId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Applies one heartbeat. Returns the stored row so the caller can report the resume point and
    /// whether this was the report that finished it.
    /// </summary>
    public async Task<PlaybackProgressDoc> RecordAsync(
        string userId,
        string contentItemId,
        double positionSeconds,
        double durationSeconds,
        double playedDeltaSeconds,
        CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, contentItemId, ct);

        // Negative positions and NaN arrive from players that have not loaded metadata yet.
        var position = Sane(positionSeconds);
        var duration = Sane(durationSeconds);

        // A duration we already knew is kept when the new report has none, so a reload that has
        // not read metadata yet does not blank it.
        if (duration <= 0) duration = existing?.DurationSeconds ?? 0;
        if (duration > 0) position = Math.Min(position, duration);

        var delta = Math.Clamp(Sane(playedDeltaSeconds), 0, MaxDeltaSeconds);
        var played = (existing?.SecondsPlayed ?? 0) + delta;

        // Played time cannot exceed the runtime. Re-listening to the same episode twice is real,
        // but counting it as eighty minutes of a forty-minute episode makes every total unusable
        // as "time spent on this".
        if (duration > 0) played = Math.Min(played, duration);

        var completedAt = existing?.CompletedAt;
        if (completedAt is null && duration > 0 && position >= duration * CompletionThreshold)
            completedAt = DateTime.UtcNow;

        var update = BuildUpdate(userId, contentItemId, position, duration, played, completedAt);

        return await _db.PlaybackProgress.FindOneAndUpdateAsync(
            p => p.UserId == userId && p.ContentItemId == contentItemId,
            update,
            new FindOneAndUpdateOptions<PlaybackProgressDoc>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After,
            },
            ct);
    }

    /// <summary>
    /// The upsert, as a value so it can be inspected without a database.
    /// </summary>
    /// <remarks>
    /// The Id has to be set on insert, and that is the whole reason this is a separate method.
    /// The filter here is a compound key — user and item — rather than the id, so on an insert
    /// Mongo has no id to derive from the filter and generates an ObjectId of its own. The Id
    /// property is a string, and the very next thing the driver does is read the document back,
    /// so every single heartbeat failed with "Cannot deserialize a 'String' from BsonType
    /// 'ObjectId'". Measured against staging, which is where it was caught.
    ///
    /// The codebase's two other update-upserts avoid this by accident rather than by rule: one
    /// sets the id explicitly, the other filters on it so Mongo derives it. A compound filter has
    /// neither of those protections.
    /// </remarks>
    public static UpdateDefinition<PlaybackProgressDoc> BuildUpdate(
        string userId,
        string contentItemId,
        double position,
        double duration,
        double played,
        DateTime? completedAt) =>
        Builders<PlaybackProgressDoc>.Update
            .Set(p => p.PositionSeconds, position)
            .Set(p => p.DurationSeconds, duration)
            .Set(p => p.SecondsPlayed, played)
            .Set(p => p.CompletedAt, completedAt)
            .Set(p => p.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(p => p.Id, Guid.NewGuid().ToString())
            .SetOnInsert(p => p.UserId, userId)
            .SetOnInsert(p => p.ContentItemId, contentItemId)
            .SetOnInsert(p => p.StartedAt, DateTime.UtcNow);

    /// <summary>Guards against NaN, infinity and negatives arriving from a media element.</summary>
    private static double Sane(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;
}

using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Whether the learner has given Radar anything to work from yet.
/// </summary>
/// <remarks>
/// Radar's three workspaces all start empty and wait to be told what someone wants, rather than
/// inferring it from their degree. Opportunities was the exception: a brand new account was shown
/// "12 opportunities for you today — Radar matched these against your focus: Computer Science",
/// with match percentages, against a focus nobody had chosen. The percentages were real
/// arithmetic on an invented premise, which is the most convincing way to be wrong.
///
/// "Filled" is deliberately generous. One saved article, one capture, one note, or one section on
/// a path is enough — this is a gate on having said *something*, not a bar to clear.
/// </remarks>
public class LearnerFootprint
{
    private readonly RadarDatabase _db;
    private readonly IRoadmapService _roadmaps;
    private readonly ILogger<LearnerFootprint> _log;

    public LearnerFootprint(RadarDatabase db, IRoadmapService roadmaps, ILogger<LearnerFootprint> log)
    {
        _db = db;
        _roadmaps = roadmaps;
        _log = log;
    }

    /// <summary>
    /// True once Learn or Intelligence has anything in it.
    /// </summary>
    public async Task<bool> HasStartedAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        try
        {
            return await HasAnythingAsync(userId, ct);
        }
        catch (Exception ex)
        {
            // Fail open. This gate decides whether to *hide* opportunities; it must never be the
            // reason a page cannot load. When it broke, the Opportunities screen showed "Could
            // not load opportunities" rather than simply showing them — a check on whether to
            // withhold content took the content down with it.
            _log.LogWarning(ex, "Footprint check failed for {UserId}; showing opportunities.", userId);
            return true;
        }
    }

    private async Task<bool> HasAnythingAsync(string userId, CancellationToken ct)
    {
        // Learn: a path they asked for, or one they have worked through.
        if (await _roadmaps.GetActiveRoadmapAsync(userId) is not null) return true;

        // Intelligence: anything they have put in themselves.
        if (await _db.SavedItems.CountDocumentsAsync(d => d.UserId == userId, limit1, ct) > 0) return true;
        if (await _db.CapturedItems.CountDocumentsAsync(d => d.UserId == userId, limit1, ct) > 0) return true;
        if (await _db.Notes.CountDocumentsAsync(d => d.UserId == userId, limit1, ct) > 0) return true;

        // Or read enough of the feed for Radar to have learned something from it.
        //
        // Inferred only. Onboarding records a Declared signal for every interest ticked at
        // signup, so counting those made the gate open the moment someone finished onboarding —
        // which is the exact inference this gate exists to stop. Verified on staging: a
        // never-onboarded account got nothing, and onboarding alone unlocked twenty matches.
        if (await _db.BehavioralSignals.CountDocumentsAsync(
                s => s.UserId == userId && s.Source == SignalSource.Inferred && s.PositiveCount > 0,
                limit1, ct) > 0) return true;

        return false;
    }

    /// <summary>Stop at the first hit: the question is whether any exists, not how many.</summary>
    private static readonly CountOptions limit1 = new() { Limit = 1 };
}

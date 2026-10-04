using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Persistent, gradually-updating model of what each user cares about.
/// One interaction is a weak signal; repeated interactions plus saves, searches and explicit
/// feedback compound into a strong signal. Explicit negative feedback overrides weak inference.
/// </summary>
public class BehavioralSignalService : IBehavioralSignalService
{
    private readonly RadarDatabase _db;

    public BehavioralSignalService(RadarDatabase db) => _db = db;

    // Action strengths per the personalization playbook.
    private static double ActionStrength(BehavioralAction action) => action switch
    {
        BehavioralAction.MoreLikeThis   => 1.0,
        BehavioralAction.Save           => 0.8,
        BehavioralAction.Complete       => 0.8,
        BehavioralAction.Apply          => 0.8,
        BehavioralAction.Search         => 0.6,
        BehavioralAction.Read           => 0.4,
        BehavioralAction.Open           => 0.2,
        BehavioralAction.Skip           => -0.1,
        BehavioralAction.LessLikeThis   => -0.8,
        BehavioralAction.NotRelevant    => -1.0,
        BehavioralAction.Hide           => -1.0,
        BehavioralAction.RemoveInterest => -1.0,
        _                               => 0
    };

    public async Task RecordAsync(string userId, string term, BehavioralAction action, SignalSource source = SignalSource.Inferred)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(term)) return;
        term = term.Trim();

        var existing = await _db.BehavioralSignals
            .Find(s => s.UserId == userId && s.Term == term && s.Source == source)
            .FirstOrDefaultAsync();

        var signal = existing ?? new BehavioralSignal
        {
            UserId = userId,
            Term = term,
            Source = source,
            Strength = source == SignalSource.Declared ? 0.7 : 0.15,
            Confidence = source == SignalSource.Declared ? 0.85 : 0.2,
        };

        var delta = ActionStrength(action);
        var isExplicit = action is BehavioralAction.MoreLikeThis or BehavioralAction.LessLikeThis
            or BehavioralAction.NotRelevant or BehavioralAction.Hide or BehavioralAction.RemoveInterest;

        if (delta > 0)
        {
            // Diminishing returns: each positive nudge adds less as strength approaches 1.
            signal.Strength = Math.Min(1, signal.Strength + delta * (1 - signal.Strength) * 0.5);
            signal.Confidence = Math.Min(1, signal.Confidence + (isExplicit ? 0.15 : 0.05));
            signal.PositiveCount++;
        }
        else if (delta < 0)
        {
            // Explicit negative feedback suppresses the signal strongly and fast.
            var suppression = isExplicit ? Math.Abs(delta) * 0.8 : Math.Abs(delta) * 0.2;
            signal.Strength = Math.Max(0, signal.Strength - suppression);
            signal.Confidence = Math.Max(0, signal.Confidence - (isExplicit ? 0.2 : 0.05));
            signal.ExplicitFeedback = Math.Max(-1, signal.ExplicitFeedback + delta);
            signal.NegativeCount++;
        }

        signal.UpdatedAt = DateTime.UtcNow;

        await _db.BehavioralSignals.ReplaceOneAsync(
            s => s.UserId == userId && s.Term == term && s.Source == source,
            signal,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task<List<BehavioralSignal>> GetSignalsAsync(string userId) =>
        await _db.BehavioralSignals.Find(s => s.UserId == userId).ToListAsync();

    public async Task<List<BehavioralSignal>> GetTopSignalsAsync(string userId, int take = 12)
    {
        var signals = await GetSignalsAsync(userId);
        return signals
            .OrderByDescending(s => DecayedStrength(s))
            .ThenByDescending(s => s.UpdatedAt)
            .Take(take)
            .ToList();
    }

    public async Task<double> GetProfileConfidenceAsync(string userId)
    {
        var signals = await GetSignalsAsync(userId);
        if (signals.Count == 0) return 0.2;

        var declared = signals.Count(s => s.Source == SignalSource.Declared);
        var inferred = signals.Count(s => s.Source == SignalSource.Inferred);
        var evidence = signals.Sum(s => s.PositiveCount);
        var confidence = 0.3
            + Math.Min(0.35, declared * 0.08)
            + Math.Min(0.25, inferred * 0.03)
            + Math.Min(0.2, evidence * 0.01);
        return Math.Clamp(confidence, 0, 1);
    }

    /// <summary>Recency decay: recent behaviour influences recommendations more than old behaviour.</summary>
    public static double DecayedStrength(BehavioralSignal signal)
    {
        var days = Math.Max(0, (DateTime.UtcNow - signal.UpdatedAt).TotalDays);
        var decay = days switch
        {
            <= 7 => 1.0,
            <= 30 => 0.75,
            <= 90 => 0.5,
            <= 180 => 0.25,
            _ => 0.1
        };
        // Explicit feedback dominates inference when negative.
        var explicitAdjust = signal.ExplicitFeedback < 0 ? 1 + signal.ExplicitFeedback : 1.0;
        return signal.Strength * decay * explicitAdjust;
    }
}

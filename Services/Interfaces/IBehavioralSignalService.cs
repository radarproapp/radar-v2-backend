using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IBehavioralSignalService
{
    /// <summary>Applies one behavioural action to the user's signal memory (gradual, weighted).</summary>
    Task RecordAsync(string userId, string term, BehavioralAction action, SignalSource source = SignalSource.Inferred);

    /// <summary>All persisted signals for a user.</summary>
    Task<List<BehavioralSignal>> GetSignalsAsync(string userId);

    /// <summary>Top signals by decayed strength, for ranking and prompt context.</summary>
    Task<List<BehavioralSignal>> GetTopSignalsAsync(string userId, int take = 12);

    /// <summary>Overall confidence Radar has in this user's model (0..1).</summary>
    Task<double> GetProfileConfidenceAsync(string userId);
}

/// <summary>Action strengths follow the personalization playbook; explicit feedback outweighs inference.</summary>
public enum BehavioralAction
{
    MoreLikeThis,
    Save,
    Complete,
    Apply,
    Search,
    Read,
    Open,
    Skip,
    LessLikeThis,
    NotRelevant,
    Hide,
    RemoveInterest
}

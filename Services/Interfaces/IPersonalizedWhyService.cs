using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IPersonalizedWhyService
{
    /// <summary>
    /// Returns a cached personalized "why" if one exists and is still fresh for this profile
    /// (goal/persona unchanged since generation); otherwise generates and caches a new one.
    /// Returns null if generation isn't possible right now (no API key, spend cap reached, or
    /// the call failed) — callers must fall back to the item's generic WhyItMatters, never crash.
    /// </summary>
    Task<UserContentWhy?> GetOrGenerateAsync(UserProfile profile, ContentItem item, CancellationToken ct = default);

    /// <summary>Records a rating even if no cache entry exists yet (e.g. the user rated the generic fallback text).</summary>
    Task RateAsync(string userId, string contentItemId, string whyTextShown, bool helpful);

    /// <summary>Ratio of helpful ratings over the trailing window — the "why helpful rate" metric (§3.3).</summary>
    Task<(long Helpful, long NotHelpful)> GetHelpfulRateAsync(int trailingDays = 30);
}

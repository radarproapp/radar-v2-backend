namespace RadarV2.Data.Documents;

/// <summary>
/// Records that a user has already been shown an opportunity in a daily batch.
///
/// Without this there is no such thing as a "daily" batch. The feed ranked a static set by a score
/// that does not change, so the same twelve items came back every day — which is exactly what was
/// reported: "I've been seeing the same opportunities since you sent me the app to test."
///
/// Keyed by user and opportunity, with the date of the batch it first appeared in, so today's
/// selection can prefer things this person has not seen while older ones move to a collection
/// rather than occupying the daily view forever.
/// </summary>
public class OpportunityImpressionDoc
{
    /// <summary>"{userId}:{opportunityId}" — makes the upsert idempotent without a second lookup.</summary>
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public string OpportunityId { get; set; } = string.Empty;

    /// <summary>UTC date (yyyy-MM-dd) of the batch it first appeared in.</summary>
    public string FirstShownOn { get; set; } = string.Empty;

    public DateTime FirstShownAt { get; set; } = DateTime.UtcNow;
    public int TimesShown { get; set; } = 1;
}

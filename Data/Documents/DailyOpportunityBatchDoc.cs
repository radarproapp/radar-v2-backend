namespace RadarV2.Data.Documents;

/// <summary>
/// One user's curated opportunity batch for one day, built ahead of time by the scheduled job.
///
/// Previously the batch was computed on every read, which made it neither daily nor cheap: the
/// same static set was ranked by the same unchanging score, so the same twelve items came back
/// forever. Precomputing on a schedule gives the batch an actual date, keeps it stable while the
/// user looks at it, and takes the per-user scoring loop off the request path.
/// </summary>
public class DailyOpportunityBatchDoc
{
    /// <summary>"{userId}:{yyyy-MM-dd}" — one batch per user per day, upserted idempotently.</summary>
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    /// <summary>UTC date the batch is for.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Opportunity ids in presentation order.</summary>
    public List<string> OpportunityIds { get; set; } = [];

    /// <summary>How many of them this user had never been shown before.</summary>
    public int NewToYouCount { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

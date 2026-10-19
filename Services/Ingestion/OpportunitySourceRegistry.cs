using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Where opportunities actually come from.
///
/// Until this existed, Radar had never acquired an opportunity in its life: the collection held
/// thirteen hardcoded seed records and the ingestion pipeline never touched it. That is why the
/// daily batch kept showing the same items — no amount of ranking work can surface something that
/// was never fetched.
///
/// Every feed below was checked by fetching it and counting items. Feeds that returned 403, 410 or
/// zero entries were dropped rather than left in hopefully: a source that silently returns nothing
/// looks identical to "no opportunities today".
/// </summary>
public static class OpportunitySourceRegistry
{
    public sealed record OpportunityFeed(
        string Name,
        string Url,
        string RegionTag,
        OpportunityType DefaultType,
        string GoalTag);

    public static readonly IReadOnlyList<OpportunityFeed> All =
    [
        // Africa and global youth opportunities: scholarships, fellowships, grants, competitions.
        new("Opportunity Desk", "https://opportunitydesk.org/feed/",
            "Global", OpportunityType.Fellowship, "Education"),

        new("Opportunities For Africans", "https://www.opportunitiesforafricans.com/feed/",
            "Africa", OpportunityType.Fellowship, "Education"),

        new("Opportunities For Youth", "https://www.opportunitiesforyouth.org/feed/",
            "Global", OpportunityType.Fellowship, "Education"),

        // Remote roles, which matter for the "find suitable remote work" outcome in the spec.
        new("We Work Remotely", "https://weworkremotely.com/categories/remote-programming-jobs.rss",
            "Remote", OpportunityType.RemoteJob, "Career"),

        new("Remotive", "https://remotive.com/remote-jobs/feed",
            "Remote", OpportunityType.RemoteJob, "Career"),
    ];

    // Checked and deliberately excluded, so nobody re-adds them hopefully:
    //   scholarshipregion.com/feed/   → 403, blocks automated fetches
    //   remoteok.com/remote-jobs.rss  → 410, feed retired
    //   youthop.com/feed              → 200 but zero entries
    //   afterschoolafrica.com/feed/   → 200 but zero entries
}

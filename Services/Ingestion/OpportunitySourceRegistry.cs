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
    /// <param name="Access">How the source is reached.</param>
    /// <param name="Rights">
    /// What Radar may show from it. No default on purpose: adding a source without saying what
    /// may be done with its listings should be something you notice while writing the line.
    /// "Free website or public portal does not automatically grant commercial redistribution
    /// rights" - and six of these were ingested and rendered for months under an assumption
    /// nobody had written down.
    /// </param>
    /// <param name="Note">Where the rights position came from, or what still needs confirming.</param>
    public sealed record OpportunityFeed(
        string Name,
        string Url,
        string RegionTag,
        OpportunityType DefaultType,
        string GoalTag,
        SourceAccess Access,
        Redistribution Rights,
        string Note = "")
    {
        /// <summary>The family this source mostly files into.</summary>
        public OpportunityFamily Family => OpportunityFamilies.Of(DefaultType);

        /// <summary>
        /// Whether anything from here may be stored and shown.
        /// </summary>
        /// <remarks>
        /// Unconfirmed counts as no. A source nobody has checked is not a source nobody objects
        /// to, and the ingestion pipeline should treat the two the same way until somebody
        /// looks.
        /// </remarks>
        public bool MayPublish => Rights is Redistribution.Permitted or Redistribution.LinkOnly;

        /// <summary>Whether the description may be stored, as opposed to only the link.</summary>
        public bool MayStoreDescription => Rights is Redistribution.Permitted;
    }

    public static readonly IReadOnlyList<OpportunityFeed> All =
    [
        // Africa and global youth opportunities: scholarships, fellowships, grants, competitions.
        new("Opportunity Desk", "https://opportunitydesk.org/feed/",
            "Global", OpportunityType.Fellowship, "Education",
            SourceAccess.Feed, Redistribution.Unconfirmed,
            "Public WordPress feed. Terms never checked - confirm before the description is shown."),

        new("Opportunities For Africans", "https://www.opportunitiesforafricans.com/feed/",
            "Africa", OpportunityType.Fellowship, "Education",
            SourceAccess.Feed, Redistribution.Unconfirmed,
            "Public feed. Terms never checked."),

        new("Opportunities For Youth", "https://www.opportunitiesforyouth.org/feed/",
            "Global", OpportunityType.Fellowship, "Education",
            SourceAccess.Feed, Redistribution.Unconfirmed,
            "Public feed. Terms never checked."),

        // Remote roles, which matter for the "find suitable remote work" outcome in the spec.
        new("We Work Remotely", "https://weworkremotely.com/categories/remote-programming-jobs.rss",
            "Remote", OpportunityType.RemoteJob, "Career",
            SourceAccess.Feed, Redistribution.Unconfirmed,
            "Commercial job board. Redistribution is the least likely to be permitted here."),

        new("Remotive", "https://remotive.com/remote-jobs/feed",
            "Remote", OpportunityType.RemoteJob, "Career",
            SourceAccess.Feed, Redistribution.Unconfirmed,
            "Commercial job board. Confirm terms before the description is shown."),

        // Jobs in Nigeria, which nothing here carried.
        //
        // Every other feed is global, pan-African or remote, so a graduate in Lagos looking for
        // work in Lagos was served scholarships and remote programming roles. Radar is built for
        // students, graduates and professionals in Africa; not one source was posting the jobs
        // most of them are actually applying for.
        //
        // 595 entries when checked, dated the same day, each linking back to the posting.
        new("Hot Nigerian Jobs", "https://www.hotnigerianjobs.com/feed/rss.xml",
            "Nigeria", OpportunityType.Job, "Career",
            SourceAccess.Feed, Redistribution.Unconfirmed,
            "Nigeria-first coverage, which is where the users are. Terms never checked."),
    ];

    // Checked and deliberately excluded, so nobody re-adds them hopefully:
    //   scholarshipregion.com/feed/   → 403, blocks automated fetches
    //   remoteok.com/remote-jobs.rss  → 410, feed retired
    //   youthop.com/feed              → 200 but zero entries
    //   afterschoolafrica.com/feed/   → 200 but zero entries
    //
    //   jobberman.com                 → publishes no job feed. /jobs/feed and /rss both return
    //                                   HTML, and the only feed it advertises is its blog
    //                                   (rss-articles.xml, one entry). Its robots.txt disallows
    //                                   /job/, /api/, /ajax/ and every filtered listing URL, so
    //                                   there is no route to its listings that it permits. It
    //                                   would need an agreement with them, not a crawler.
    //
    //   myjobmag.com                  → advertises no feed; /feed and /jobs-rss-feed are 404.
    //   ngcareers.com                 → /feed is an HTML page; the feed it advertises belongs to
    //                                   a third-party widget, not to its listings.
}

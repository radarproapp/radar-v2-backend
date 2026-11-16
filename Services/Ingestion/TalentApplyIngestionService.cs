using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Brings TalentApply's listings into the opportunity collection.
/// </summary>
/// <remarks>
/// Separate from <see cref="OpportunityIngestionService"/> because that one parses RSS and this is
/// a JSON API behind a key. What they share is the rule that matters: a listing is stored under
/// the rights its source was recorded with, and TalentApply is the only source so far whose rights
/// were confirmed rather than assumed.
/// </remarks>
public class TalentApplyIngestionService
{
    /// <summary>
    /// What Radar asks TalentApply for, and what each answer is filed as.
    /// </summary>
    /// <remarks>
    /// Four queries rather than everything they have. Radar is for students, graduates and
    /// professionals in Africa, so graduate schemes, internships, roles in Nigeria and remote work
    /// are the four that match somebody here; pulling the whole board would spend the request
    /// budget on roles nobody using Radar can take.
    ///
    /// country_id would be the better filter and their spec says so, but it takes their internal
    /// country id and publishes no endpoint to look one up. The example value in the spec is an
    /// example, not Nigeria, so this uses the documented fuzzy location match rather than guessing
    /// an id and silently fetching the wrong country's jobs.
    /// </remarks>
    /// <param name="Region">
    /// What to tag a listing from this query when its own text does not say.
    /// </param>
    /// <remarks>
    /// The region comes from the query, not from the source. Falling back to the source's own
    /// "Global" tag meant a job the Nigeria query returned, whose location reads "Lagos", was
    /// stored as Global - and the nearby check compares the tag, so it was then excluded from the
    /// feed of the very person the query was run for. The query already knows what it asked for.
    /// </remarks>
    private static readonly (string Label, Dictionary<string, string> Filters, OpportunityType Type, string Region)[] Queries =
    [
        ("graduate schemes", new() { ["category"] = "graduate" }, OpportunityType.GraduateProgramme, "Global"),
        ("internships", new() { ["category"] = "internship" }, OpportunityType.Internship, "Global"),
        ("roles in Nigeria", new() { ["location"] = "Nigeria" }, OpportunityType.Job, "Nigeria"),
        ("remote roles", new() { ["work_arrangement"] = "remote" }, OpportunityType.RemoteJob, "Remote"),
    ];

    /// <summary>Only listings posted recently, so a run is not re-reading last year's board.</summary>
    /// <remarks>
    /// Sixty rather than thirty. A window this wide is still a bound, but it stops Radar being
    /// blind to a role that is open and was posted seven weeks ago - and nothing in their API
    /// says a listing stops being live at thirty days. The logged total per query is what settles
    /// whether this is the right width.
    /// </remarks>
    private const string PostedWithinDays = "60";

    private readonly ITalentApplyJobs _client;
    private readonly RadarDatabase _db;
    private readonly ILogger<TalentApplyIngestionService> _log;

    public TalentApplyIngestionService(
        ITalentApplyJobs client,
        RadarDatabase db,
        ILogger<TalentApplyIngestionService> log)
    {
        _client = client;
        _db = db;
        _log = log;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!_client.IsConfigured)
        {
            // Information, not Debug. Debug sits below the default production level, so setting
            // TalentApply_ApiKey instead of TalentApply__ApiKey produced a system that looked
            // healthy and silently ingested nothing from the one source under agreement.
            _log.LogInformation(
                "TalentApply API key not configured — skipping. Set TalentApply__ApiKey to enable it.");
            return 0;
        }

        var source = OpportunitySourceRegistry.Partners.Single(feed => feed.Name == "TalentApply");
        var added = 0;
        var requests = 0;
        var skipped = 0;
        var reportedFields = false;

        foreach (var (label, filters, type, region) in Queries)
        {
            if (ct.IsCancellationRequested) break;

            var page = 1;
            var lastPage = 1;
            var pagesRead = 0;
            var reportedTotal = 0;

            while (page <= lastPage
                   && pagesRead < TalentApplyClient.MostPagesPerQuery
                   && requests < TalentApplyClient.MostRequestsPerRun)
            {
                if (ct.IsCancellationRequested) break;

                var query = new Dictionary<string, string>(filters) { ["posted_within"] = PostedWithinDays };

                requests++;
                var result = await _client.FetchJobsAsync(query, page, ct);

                // Null is a failed request, not an exhausted source. Moving to the next query
                // rather than the next page, because a source that just refused one call is
                // unlikely to answer the one after it.
                if (result is null) break;

                lastPage = result.LastPage;
                reportedTotal = result.Total;
                pagesRead++;

                // A page with nothing on it ends the query. last_page can report thirty while
                // page two comes back empty, and without this the loop asks for pages three to
                // thirty, spending the allowance on nothing.
                if (result.Jobs.Count == 0) break;

                // Reports only while nothing has read cleanly, which is what the comment used to
                // claim and the code did not do. It armed on the first listing either way, so a
                // readable Jobs[0] followed by a board Radar could not parse was never reported,
                // and a later page in a different shape never got a second look.
                if (!reportedFields)
                {
                    if (result.Jobs.Any(job => job.IsUsable))
                    {
                        reportedFields = true;
                    }
                    else if (result.Jobs.Count > 0)
                    {
                        _log.LogWarning(
                            "TalentApply listings could not be read with the inferred field names. "
                            + "Fields actually present: {Fields}",
                            string.Join(", ", result.Jobs[0].FieldNames));
                    }
                }

                foreach (var job in result.Jobs)
                {
                    if (ct.IsCancellationRequested) break;

                    // No title or no attributed link means no row. A listing stored without its
                    // view_url reaches somebody's feed, earns the referral for nobody, and looks
                    // exactly like one that worked.
                    if (!job.IsUsable)
                    {
                        skipped++;
                        continue;
                    }

                    var url = job.AttributedUrl!;

                    var exists = await _db.Opportunities.Find(o => o.Url == url).AnyAsync(ct);
                    if (exists) continue;

                    try
                    {
                        await _db.Opportunities.InsertOneAsync(new Opportunity
                        {
                            Type = type,
                            Title = FeedText.Shorten(FeedText.Clean(job.Title), 160),

                            // Empty rather than the board's own name. "Data Analyst at TalentApply"
                            // names the aggregator as the employer, which is wrong on the screen and
                            // likely whenever the inferred company field misses; the card omits the
                            // line when there is nothing to put in it.
                            Organisation = FeedText.Clean(job.Organisation),

                            // Stored because the agreement permits it: the first source to clear
                            // MayStoreDescription, and the only reason this field is not empty.
                            // Cleaned for the same reason - descriptions come out of an ATS as
                            // HTML, so the one source the rights gate lets through was also the
                            // only one that could put literal markup in front of a reader.
                            Description = job.Description is { } body && source.MayStoreDescription
                                ? FeedText.Shorten(FeedText.Clean(body), 600)
                                : string.Empty,

                            Url = url,
                            Location = job.Location,
                            IsRemote = string.Equals(job.WorkArrangement, "remote", StringComparison.OrdinalIgnoreCase)
                                || type == OpportunityType.RemoteJob,
                            RegionTag = job.Location is { } where && where.Contains("Nigeria", StringComparison.OrdinalIgnoreCase)
                                ? "Nigeria"
                                : region,
                            GoalTag = source.GoalTag,
                            Deadline = job.Deadline,
                            DiscoveredAt = DateTime.UtcNow,
                            Rights = source.Rights,

                            // Same rule as every other source: eligibility, benefits and selection
                            // process are specific claims, and inventing them from a listing is how
                            // somebody applies for a thing they do not qualify for.
                            Benefits = string.Empty,
                            WhoIsEligible = [],
                        }, cancellationToken: ct);

                        added++;
                    }
                    catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        // Somebody else stored it between the check and the write. Url carries a
                        // unique index, so the check-then-act above has a window, and two runs
                        // overlap routinely - the in-process timer and the deploy's cron, or two
                        // replicas. Uncaught this unwound out of the whole run, so one lost race
                        // cost every remaining listing, page and query for the night.
                    }
                }

                page++;
            }

            // Per query, because the totals are what say whether the window and the page
            // allowance are the binding constraint or their catalogue is. "Read 15 of 40 pages"
            // is the line that asks for a bigger budget; "read 3 of 3" is the one that says the
            // budget is already generous.
            _log.LogInformation(
                "TalentApply {Query}: read {Pages} of {LastPage} pages, {Total} listings available.",
                label, pagesRead, lastPage, reportedTotal);
        }

        // Skipped is in the line because without it "0 new from 20 requests" reads identically
        // whether every listing was already stored, which is the normal steady state, or none of
        // them could be read at all, which is the emergency.
        _log.LogInformation(
            "TalentApply: {Added} new opportunities from {Requests} requests, {Skipped} listings unreadable.",
            added, requests, skipped);
        return added;
    }

}

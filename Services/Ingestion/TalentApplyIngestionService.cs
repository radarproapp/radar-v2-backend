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
    private static readonly (string Label, Dictionary<string, string> Filters, OpportunityType Type)[] Queries =
    [
        ("graduate schemes", new() { ["category"] = "graduate" }, OpportunityType.GraduateProgramme),
        ("internships", new() { ["category"] = "internship" }, OpportunityType.Internship),
        ("roles in Nigeria", new() { ["location"] = "Nigeria" }, OpportunityType.Job),
        ("remote roles", new() { ["work_arrangement"] = "remote" }, OpportunityType.RemoteJob),
    ];

    /// <summary>Only listings posted recently, so a run is not re-reading last year's board.</summary>
    private const string PostedWithinDays = "30";

    private readonly TalentApplyClient _client;
    private readonly RadarDatabase _db;
    private readonly ILogger<TalentApplyIngestionService> _log;

    public TalentApplyIngestionService(
        TalentApplyClient client,
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
            _log.LogDebug("TalentApply API key not configured — skipping.");
            return 0;
        }

        var source = OpportunitySourceRegistry.Partners.Single(feed => feed.Name == "TalentApply");
        var added = 0;
        var requests = 0;
        var reportedFields = false;

        foreach (var (label, filters, type) in Queries)
        {
            if (ct.IsCancellationRequested) break;

            var page = 1;
            var lastPage = 1;

            while (page <= lastPage && requests < TalentApplyClient.MostRequestsPerRun)
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

                // Once per run, and only while the mapping is still provisional. Their job schema
                // is published empty, so this is what turns the inferred field names into known
                // ones - and it stops as soon as a listing reads cleanly.
                if (!reportedFields && result.Jobs.Count > 0)
                {
                    reportedFields = true;
                    var first = result.Jobs[0];
                    if (!first.IsUsable)
                    {
                        _log.LogWarning(
                            "TalentApply listings could not be read with the inferred field names. "
                            + "Fields actually present: {Fields}",
                            string.Join(", ", first.FieldNames));
                    }
                }

                foreach (var job in result.Jobs)
                {
                    if (ct.IsCancellationRequested) break;

                    // No title or no attributed link means no row. A listing stored without its
                    // view_url reaches somebody's feed, earns the referral for nobody, and looks
                    // exactly like one that worked.
                    if (!job.IsUsable) continue;

                    var url = job.AttributedUrl!;

                    var exists = await _db.Opportunities.Find(o => o.Url == url).AnyAsync(ct);
                    if (exists) continue;

                    await _db.Opportunities.InsertOneAsync(new Opportunity
                    {
                        Type = type,
                        Title = Shorten(job.Title!, 160),
                        Organisation = job.Organisation ?? "TalentApply",

                        // Stored because the agreement permits it. This is the first source that
                        // clears MayStoreDescription, and the only reason the field is not empty.
                        Description = job.Description is { } body && source.MayStoreDescription
                            ? Shorten(body, 600)
                            : string.Empty,

                        Url = url,
                        Location = job.Location,
                        IsRemote = string.Equals(job.WorkArrangement, "remote", StringComparison.OrdinalIgnoreCase)
                            || type == OpportunityType.RemoteJob,
                        RegionTag = job.Location is { } where && where.Contains("Nigeria", StringComparison.OrdinalIgnoreCase)
                            ? "Nigeria"
                            : source.RegionTag,
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

                page++;
            }
        }

        _log.LogInformation(
            "TalentApply: {Added} new opportunities from {Requests} requests.", added, requests);
        return added;
    }

    private static string Shorten(string text, int most) =>
        text.Length <= most ? text : text[..most].TrimEnd() + "…";
}

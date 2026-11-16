using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests.Integration;

/// <summary>
/// What a run actually writes, and what it refuses to.
/// </summary>
/// <remarks>
/// None of this had a test. The client was a concrete class with no seam, so the paging loop, the
/// per-query budget, the region tagging and the skip rules were reachable only by making real
/// HTTP calls - and every one of those is where the review found a defect. A fake that answers
/// pages, against a real Mongo, reaches all of them.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class TalentApplyIngestionTests
{
    private readonly RadarDatabaseFixture _fixture;

    public TalentApplyIngestionTests(RadarDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>A stand-in for TalentApply that answers from a script.</summary>
    private sealed class FakeJobs : ITalentApplyJobs
    {
        private readonly Func<IReadOnlyDictionary<string, string>, int, TalentApplyPage?> _answer;

        public FakeJobs(Func<IReadOnlyDictionary<string, string>, int, TalentApplyPage?> answer) =>
            _answer = answer;

        public bool IsConfigured { get; init; } = true;

        public List<(string Filters, int Page)> Calls { get; } = [];

        public Task<TalentApplyPage?> FetchJobsAsync(
            IReadOnlyDictionary<string, string> filters, int page, CancellationToken ct)
        {
            Calls.Add((string.Join(",", filters.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}")), page));
            return Task.FromResult(_answer(filters, page));
        }
    }

    private static TalentApplyJob Job(string json) =>
        new(JsonDocument.Parse(json).RootElement.Clone());

    private static TalentApplyJob Listing(string id, string? location = null) =>
        Job($$"""
            {
              "job_title": "Role {{id}}",
              "company_name": "Employer {{id}}",
              "description": "<p>Work on <b>things</b> &amp; more</p>",
              "view_url": "https://app.talentapply.io/j/{{id}}?ref=radar"
              {{(location is null ? "" : $", \"location\": \"{location}\"")}}
            }
            """);

    private TalentApplyIngestionService Service(ITalentApplyJobs jobs, out RadarV2.Data.RadarDatabase db)
    {
        // Short on purpose. The fixture builds "radar-test-{name}-{32-char guid}", and Mongo
        // refuses a database name over 63 characters - which the class name alone overran.
        db = _fixture.Database("talentapply");
        return new TalentApplyIngestionService(
            jobs, db, NullLogger<TalentApplyIngestionService>.Instance);
    }

    [Fact]
    public async Task NoKeyMeansNoWritesAndNoCalls()
    {
        var jobs = new FakeJobs((_, _) => null) { IsConfigured = false };
        var service = Service(jobs, out var db);

        Assert.Equal(0, await service.RunAsync());
        Assert.Empty(jobs.Calls);
        Assert.Equal(0, await db.Opportunities.CountDocumentsAsync(FilterDefinition<Opportunity>.Empty));
    }

    /// <summary>
    /// A deep query does not consume the allowance the other three need.
    /// </summary>
    /// <remarks>
    /// The defect that would have hurt most, and the one that could not be seen from the outside.
    /// With a shared pool the first query took everything and the rest never ran - and because
    /// paging restarts at one each night with no cursor, that is permanent rather than a bad
    /// night: every later run re-reads the same pages and never reaches the others.
    /// </remarks>
    [Fact]
    public async Task ADeepFirstQueryDoesNotStarveTheRest()
    {
        var counter = 0;

        // Every query claims to be a thousand pages deep, so nothing ends naturally.
        var jobs = new FakeJobs((_, page) =>
            new TalentApplyPage([Listing($"x{Interlocked.Increment(ref counter)}")], LastPage: 1000, Total: 50_000));

        var service = Service(jobs, out _);
        await service.RunAsync();

        var queried = jobs.Calls
            .Select(call => call.Filters)
            .Distinct()
            .Count();

        Assert.Equal(4, queried);

        foreach (var group in jobs.Calls.GroupBy(call => call.Filters))
            Assert.Equal(TalentApplyClient.MostPagesPerQuery, group.Count());
    }

    /// <summary>An empty page ends that query instead of spending the allowance on nothing.</summary>
    [Fact]
    public async Task AnEmptyPageEndsTheQuery()
    {
        var jobs = new FakeJobs((_, page) => page == 1
            ? new TalentApplyPage([Listing("a")], LastPage: 30, Total: 900)
            : new TalentApplyPage([], LastPage: 30, Total: 900));

        var service = Service(jobs, out _);
        await service.RunAsync();

        var queries = jobs.Calls.GroupBy(call => call.Filters).ToList();

        // Asserted before the loop. Every assertion here used to live inside it, so a run that
        // made no calls at all - the failure most worth catching - produced no iterations and
        // passed.
        Assert.Equal(4, queries.Count);

        // Two pages per query: the one with an item, then the empty one that stops it.
        foreach (var group in queries)
            Assert.Equal(2, group.Count());
    }

    /// <summary>A failed request moves to the next query rather than paging on.</summary>
    [Fact]
    public async Task AFailedRequestEndsThatQueryOnly()
    {
        var jobs = new FakeJobs((filters, _) =>
            filters.TryGetValue("category", out var category) && category == "graduate"
                ? null
                // A distinct listing per query. Given the same one, all three dedupe to a single
                // row on view_url - correctly - and the count says nothing about how many queries
                // ran, which is what this test is actually asking.
                : new TalentApplyPage(
                    [Listing(string.Concat(filters.Where(f => f.Key != "posted_within").Select(f => f.Value)))],
                    LastPage: 1, Total: 1));

        var service = Service(jobs, out _);
        var added = await service.RunAsync();

        Assert.Equal(3, added);
        Assert.Single(jobs.Calls, call => call.Filters.Contains("category=graduate"));
    }

    /// <summary>
    /// A listing with no attributed link is never stored.
    /// </summary>
    /// <remarks>
    /// The rule the whole agreement rests on. A listing stored without its view_url still opens,
    /// still looks right, and earns the referral for nobody.
    /// </remarks>
    [Fact]
    public async Task AListingWithoutAnAttributedLinkIsNotStored()
    {
        var jobs = new FakeJobs((_, _) => new TalentApplyPage(
            [
                Job("""{"job_title":"No link","apply_url":"https://employer.example.com/apply"}"""),
                Listing("good"),
            ],
            LastPage: 1, Total: 2));

        var service = Service(jobs, out var db);
        await service.RunAsync();

        var stored = await db.Opportunities.Find(FilterDefinition<Opportunity>.Empty).ToListAsync();

        // The count first. Assert.All and Assert.DoesNotContain both pass over an empty list, so
        // an ingester that stored nothing at all satisfied every assertion this test used to make.
        Assert.Single(stored);

        Assert.All(stored, o => Assert.StartsWith("https://app.talentapply.io/j/", o.Url));
        Assert.DoesNotContain(stored, o => o.Title == "No link");
    }

    /// <summary>
    /// A job from the Nigeria query is tagged Nigeria, not Global.
    /// </summary>
    /// <remarks>
    /// Tagged from the source it was stored as Global, and the nearby check compares the tag - so
    /// a Lagos job fetched specifically for Nigerian users was then excluded from their feed. The
    /// query knows what it asked for; the listing's own text may only say "Lagos".
    /// </remarks>
    [Fact]
    public async Task AJobFromTheNigeriaQueryIsTaggedNigeria()
    {
        var jobs = new FakeJobs((filters, page) => page > 1
            ? new TalentApplyPage([], 1, 0)
            : new TalentApplyPage(
                [Listing(filters.ContainsKey("location") ? "ng" : "other", location: "Lagos")],
                LastPage: 1, Total: 1));

        var service = Service(jobs, out var db);
        await service.RunAsync();

        var nigerian = await db.Opportunities
            .Find(o => o.Url == "https://app.talentapply.io/j/ng?ref=radar")
            .FirstAsync();

        Assert.Equal("Nigeria", nigerian.RegionTag);
    }

    /// <summary>
    /// What is stored carries the source's rights, and a cleaned description.
    /// </summary>
    /// <remarks>
    /// TalentApply is the only source that clears MayStoreDescription, so it is the only one whose
    /// descriptions are stored at all - and was briefly the only one storing them as raw ATS HTML.
    /// </remarks>
    [Fact]
    public async Task AStoredListingCarriesItsRightsAndACleanedDescription()
    {
        var jobs = new FakeJobs((_, page) => page > 1
            ? new TalentApplyPage([], 1, 0)
            : new TalentApplyPage([Listing("a")], LastPage: 1, Total: 1));

        var service = Service(jobs, out var db);
        await service.RunAsync();

        var stored = await db.Opportunities.Find(FilterDefinition<Opportunity>.Empty).FirstAsync();

        Assert.Equal(Redistribution.Permitted, stored.Rights);
        Assert.Equal("Work on things & more", stored.Description);
        Assert.DoesNotContain("<", stored.Description);
        Assert.Equal("Employer a", stored.Organisation);
        Assert.Empty(stored.WhoIsEligible);
    }

    /// <summary>Running twice stores each listing once.</summary>
    [Fact]
    public async Task RunningTwiceDoesNotDuplicateAnything()
    {
        var jobs = new FakeJobs((_, page) => page > 1
            ? new TalentApplyPage([], 1, 0)
            : new TalentApplyPage([Listing("a"), Listing("b")], LastPage: 1, Total: 2));

        var service = Service(jobs, out var db);

        var first = await service.RunAsync();
        var second = await service.RunAsync();

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(first, await db.Opportunities.CountDocumentsAsync(FilterDefinition<Opportunity>.Empty));
    }
}

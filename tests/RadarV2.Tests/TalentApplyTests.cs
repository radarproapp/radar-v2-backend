using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// The first source Radar reads with permission rather than an assumption.
/// </summary>
/// <remarks>
/// TalentApply publishes a complete spec for every resource except the one describing a job, so
/// the envelope is parsed strictly and the listing is read defensively. These tests pin both
/// halves: the envelope must be read exactly as documented, and a listing that cannot be read must
/// be skipped rather than stored half-empty.
/// </remarks>
public class TalentApplyTests
{
    private static TalentApplyJob Job(string json) =>
        new(JsonDocument.Parse(json).RootElement.Clone());

    // ── the envelope, which is documented ────────────────────────────────────

    [Fact]
    public void TheDocumentedEnvelopeIsReadAsSpecified()
    {
        var page = TalentApplyClient.Parse(
            """
            {
              "message": "Jobs retrieved",
              "data": {
                "items": [
                  { "job_title": "Data Analyst", "view_url": "https://app.talentapply.io/j/abc?ref=radar" },
                  { "job_title": "Product Designer", "view_url": "https://app.talentapply.io/j/def?ref=radar" }
                ],
                "pagination": { "current_page": 1, "last_page": 4, "total": 180, "per_page": 50 }
              },
              "success": true,
              "code": 200
            }
            """,
            NullLogger.Instance);

        Assert.NotNull(page);
        Assert.Equal(2, page.Jobs.Count);
        Assert.Equal(4, page.LastPage);
        Assert.Equal(180, page.Total);
        Assert.Equal("Data Analyst", page.Jobs[0].Title);

        // Through the envelope, not just on a standalone listing. The attributed link is the
        // thing this integration earns from, so it is pinned across the boundary too.
        Assert.Equal("https://app.talentapply.io/j/abc?ref=radar", page.Jobs[0].AttributedUrl);
        Assert.True(page.Jobs.All(job => job.IsUsable));
    }

    /// <summary>
    /// The budget is per query, so one deep query cannot starve the rest.
    /// </summary>
    /// <remarks>
    /// Shared and consumed in order, a query with enough pages took the whole allowance and the
    /// queries after it never ran - internships and Nigerian roles going unread on any night
    /// graduate schemes happened to be deep, logged as a successful run. And because paging
    /// restarts at one every run with no cursor, that is not one bad night: every later run
    /// re-reads the same pages, stores nothing new, and never reaches the other three.
    ///
    /// Pinned as arithmetic because the alternative is a test that fires sixty HTTP requests.
    /// </remarks>
    [Fact]
    public void EveryQueryGetsItsOwnPageAllowanceWithinTheRunBackstop()
    {
        const int queries = 4;

        // The backstop must sit above what the four queries can spend between them, or it binds
        // first and the last query is starved by the run total instead of by a shared pool -
        // the same defect wearing a different hat.
        //
        // The inequality here was the wrong way round when the numbers happened to be equal,
        // which is the kind of mistake equal numbers hide: it asserted the backstop should be at
        // or below the allowances, and passed only because 4 x 15 was exactly 60.
        Assert.True(
            TalentApplyClient.MostRequestsPerRun >= queries * TalentApplyClient.MostPagesPerQuery,
            "The run backstop is below the per-query allowances, so the last query is starved again.");

        // No cap against 120 here. That limit is 120 requests per minute, not per run, and a run
        // is sequential: the first production run spent about two minutes per query on fifteen
        // pages, so even 170 requests are spread across far more than a minute. Capping the run
        // total was reading a rate limit as a quota.
        Assert.True(
            TalentApplyClient.MostPagesPerQuery > 0 && queries * TalentApplyClient.MostPagesPerQuery > 0,
            "Every query needs a non-zero page allowance.");
    }

    /// <summary>A page without a reported total still reports something sane.</summary>
    [Fact]
    public void AMissingTotalFallsBackToWhatWasActuallyReturned()
    {
        var page = TalentApplyClient.Parse(
            """{"data":{"items":[{"title":"A","view_url":"https://x.co/a"}],"pagination":{"last_page":1}}}""",
            NullLogger.Instance);

        Assert.NotNull(page);
        Assert.Equal(1, page.Total);
    }

    /// <summary>
    /// A pagination field of the wrong type does not take the run down.
    /// </summary>
    /// <remarks>
    /// TryGetInt32 throws for a non-number rather than returning false, and only JsonException was
    /// caught - so a last_page arriving as "4" or null, which is ordinary serialiser drift, threw
    /// straight out through the paging loop. The one branch that did not degrade was the one in
    /// the method whose whole job is surviving a contract change.
    /// </remarks>
    [Theory]
    [InlineData("""{"data":{"items":[],"pagination":{"last_page":"4","total":"180"}}}""")]
    [InlineData("""{"data":{"items":[],"pagination":{"last_page":null,"total":null}}}""")]
    [InlineData("""{"data":{"items":[],"pagination":{"last_page":{"x":1},"total":[1]}}}""")]
    public void PaginationOfTheWrongTypeIsSurvivedRatherThanThrown(string body)
    {
        var page = TalentApplyClient.Parse(body, NullLogger.Instance);

        Assert.NotNull(page);
        Assert.Equal(1, page.LastPage);
    }

    /// <summary>Entries that are not objects are dropped rather than crashing the page.</summary>
    [Fact]
    public void NonObjectEntriesInTheItemsArrayAreIgnored()
    {
        var page = TalentApplyClient.Parse(
            """{"data":{"items":[null,1,"x",{"title":"Real","view_url":"https://x.co/a"}]}}""",
            NullLogger.Instance);

        Assert.NotNull(page);
        Assert.Single(page.Jobs);
        Assert.Equal("Real", page.Jobs[0].Title);
    }

    /// <summary>
    /// A body without data.items is a contract change, not an empty result.
    /// </summary>
    /// <remarks>
    /// The difference decides what the paging loop does next. Returning an empty page would read
    /// as "this source has nothing today" and be logged as a normal quiet run, which is how a
    /// source goes silently dead for a month.
    /// </remarks>
    [Theory]
    [InlineData("""{"message":"Jobs retrieved","success":true,"code":200}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"items":{}}}""")]
    [InlineData("not json at all")]
    public void ABodyThatIsNotTheDocumentedShapeIsNotAnEmptyResult(string body) =>
        Assert.Null(TalentApplyClient.Parse(body, NullLogger.Instance));

    [Fact]
    public void APageWithNoListingsIsStillAPage()
    {
        var page = TalentApplyClient.Parse(
            """{"data":{"items":[],"pagination":{"current_page":1,"last_page":1,"total":0,"per_page":50}}}""",
            NullLogger.Instance);

        Assert.NotNull(page);
        Assert.Empty(page.Jobs);
    }

    // ── the listing, which is not ────────────────────────────────────────────

    /// <summary>
    /// Attribution is never substituted with a link that works and earns nothing.
    /// </summary>
    /// <remarks>
    /// The whole value of the agreement is that a candidate arriving through view_url is credited
    /// to Radar automatically. apply_url and a slug-built link reach the same posting, so falling
    /// back to one would look perfect in every test that checks the listing opens - and quietly
    /// give away the referral this integration exists to earn.
    /// </remarks>
    [Fact]
    public void AListingWithNoAttributedLinkIsNotUsableEvenWhenAnotherLinkExists()
    {
        var job = Job("""
            {
              "job_title": "Backend Engineer",
              "apply_url": "https://employer.example.com/apply/123",
              "job_slug": "backend-engineer-123"
            }
            """);

        Assert.Equal("Backend Engineer", job.Title);
        Assert.Null(job.AttributedUrl);
        Assert.False(job.IsUsable);
    }

    [Fact]
    public void AListingNeedsBothATitleAndAnAttributedLink()
    {
        Assert.False(Job("""{"view_url":"https://app.talentapply.io/j/abc"}""").IsUsable);
        Assert.False(Job("""{"job_title":"Analyst"}""").IsUsable);
        Assert.True(Job("""{"job_title":"Analyst","view_url":"https://app.talentapply.io/j/abc"}""").IsUsable);
    }

    /// <summary>Named things arrive as objects, and a person should not be shown an id.</summary>
    [Fact]
    public void ANestedNamedResourceIsReadByItsName()
    {
        var job = Job("""
            {
              "title": "Analyst",
              "view_url": "https://app.talentapply.io/j/abc",
              "location": { "id": 160, "name": "Lagos, Nigeria" }
            }
            """);

        Assert.Equal("Lagos, Nigeria", job.Location);
    }

    /// <summary>
    /// What the listing actually contained, so the inferred names can be replaced with real ones.
    /// </summary>
    /// <remarks>
    /// Names only. This goes to the application log, and the contents of a listing are
    /// TalentApply's to publish, not Radar's to copy into its own logs.
    /// </remarks>
    [Fact]
    public void TheFieldsActuallyPresentCanBeReportedWithoutTheirValues()
    {
        var job = Job("""{"some_unexpected_name":"Analyst","another":"https://example.com"}""");

        Assert.False(job.IsUsable);
        Assert.Equal(["some_unexpected_name", "another"], job.FieldNames);
    }

    /// <summary>
    /// A date with no timezone is read as UTC, not as the server's local time.
    /// </summary>
    /// <remarks>
    /// AdjustToUniversal without AssumeUniversal reads an offset-less date as local and then
    /// shifts it, so on a UTC+1 host a 1 March deadline is stored as 28 February 23:00Z. Every
    /// deadline-derived answer - HasClosed, DaysUntilDeadline, the urgency label - then fires a
    /// day early, and somebody is told a listing that is open has closed.
    ///
    /// Asserted as an exact instant and an exact Kind. The previous version of this test compared
    /// .Date on a Z-suffixed input, which absorbs any sub-day shift and passed against the broken
    /// implementation on every UTC+ host.
    /// </remarks>
    [Theory]
    [InlineData("2027-03-01T00:00:00Z")]
    [InlineData("2027-03-01")]
    [InlineData("2027-03-01 00:00:00")]
    public void ADeadlineWithoutATimezoneIsStillReadAsUtc(string raw)
    {
        var job = Job($$"""{"title":"Analyst","view_url":"https://x.co/a","job_deadline_at":"{{raw}}"}""");

        Assert.Equal(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), job.Deadline);
        Assert.Equal(DateTimeKind.Utc, job.Deadline!.Value.Kind);
    }

    /// <summary>A day-first date means the same day wherever the server is.</summary>
    /// <remarks>
    /// Read with a null provider it took the host's culture, so the same listing was a March
    /// deadline on one machine and a January one on another.
    /// </remarks>
    [Fact]
    public void ADateIsReadTheSameWayRegardlessOfTheHostCulture()
    {
        var job = Job("""{"title":"A","view_url":"https://x.co/a","job_deadline_at":"03/01/2027"}""");

        Assert.Equal(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), job.Deadline);
    }

    [Fact]
    public void AListingWithNoDeadlineHasNone() =>
        Assert.Null(Job("""{"title":"Analyst","view_url":"https://x.co/a"}""").Deadline);

    /// <summary>
    /// A nested field of an unexpected type is skipped, not thrown from.
    /// </summary>
    /// <remarks>
    /// GetString throws for any kind but String and Null, so a listing carrying
    /// {"location":{"name":160}} threw from inside IsUsable - in the per-listing loop, with no
    /// per-listing guard, ending every remaining listing, page and query. Precisely the "the
    /// inferred names were wrong" case the reader exists to absorb, and it crashed instead.
    /// </remarks>
    [Theory]
    [InlineData("""{"title":"A","view_url":"https://x.co/a","location":{"id":160,"name":160}}""")]
    [InlineData("""{"title":"A","view_url":"https://x.co/a","location":{"name":{"en":"Lagos"}}}""")]
    [InlineData("""{"title":"A","view_url":"https://x.co/a","location":{"name":null}}""")]
    [InlineData("""{"title":"A","view_url":"https://x.co/a","location":[1,2]}""")]
    public void ANestedFieldOfTheWrongTypeDoesNotThrow(string json)
    {
        var job = Job(json);

        Assert.Null(job.Location);
        Assert.True(job.IsUsable);
    }

    // ── the source register ──────────────────────────────────────────────────

    /// <summary>
    /// TalentApply is the one source so far whose rights were confirmed rather than assumed.
    /// </summary>
    [Fact]
    public void TalentApplyIsRegisteredAsAPermittedNegotiatedSource()
    {
        var source = OpportunitySourceRegistry.Partners.Single(feed => feed.Name == "TalentApply");

        Assert.Equal(SourceAccess.Negotiated, source.Access);
        Assert.Equal(Redistribution.Permitted, source.Rights);
        Assert.True(source.MayBeIngested);
        Assert.True(source.MayPublish);
        Assert.True(source.MayStoreDescription);
    }

    /// <summary>
    /// A partner is not reached by the RSS ingester, and is still covered by the rights rules.
    /// </summary>
    /// <remarks>
    /// The two lists are split by how a source is read, not by how much it is trusted. Were
    /// Partners left out of the rights tests, the split would have become a way to add a source
    /// without stating its position - which is the exact hole the register was built to close.
    /// </remarks>
    [Fact]
    public void PartnersAreOutsideTheFeedListButInsideTheRightsRules()
    {
        Assert.DoesNotContain(OpportunitySourceRegistry.All, feed => feed.Name == "TalentApply");
        Assert.Contains(OpportunitySourceRegistry.Everything, feed => feed.Name == "TalentApply");

        Assert.Equal(
            OpportunitySourceRegistry.All.Count + OpportunitySourceRegistry.Partners.Count,
            OpportunitySourceRegistry.Everything.Count);

        foreach (var feed in OpportunitySourceRegistry.Everything)
            Assert.False(string.IsNullOrWhiteSpace(feed.Note), $"{feed.Name} states no rights position.");
    }

    /// <summary>The registry initialises at all.</summary>
    /// <remarks>
    /// Everything spreads All, and static field initialisers run in the order they are written.
    /// Declared above All it read a null and threw in the type initialiser - which compiles
    /// cleanly, passes every test that does not touch the registry, and fails on first use.
    /// </remarks>
    [Fact]
    public void TheRegistryHasNoStaticInitialisationOrderProblem()
    {
        Assert.NotEmpty(OpportunitySourceRegistry.Everything);
        Assert.NotEmpty(OpportunitySourceRegistry.All);
        Assert.NotEmpty(OpportunitySourceRegistry.Partners);
    }
}

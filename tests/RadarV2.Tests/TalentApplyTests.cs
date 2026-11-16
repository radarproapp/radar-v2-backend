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
        Assert.Equal("Data Analyst", page.Jobs[0].Title);
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

    [Fact]
    public void ADeadlineIsReadWhenTheListingCarriesOne()
    {
        var job = Job("""
            {"title":"Analyst","view_url":"https://x.co/a","job_deadline_at":"2027-03-01T00:00:00Z"}
            """);

        Assert.Equal(new DateTime(2027, 3, 1), job.Deadline?.Date);
        Assert.Null(Job("""{"title":"Analyst","view_url":"https://x.co/a"}""").Deadline);
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

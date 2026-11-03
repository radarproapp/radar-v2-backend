using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Reading the events Radar has always recorded and never looked at.
/// </summary>
/// <remarks>
/// Eighteen kinds of event were being written - impressions, opens, saves, dismissals, reports -
/// and the only readers were the daily digest and the user graph. Nothing in the admin area
/// touched any of them, so "what are people engaging with" was a question the database could
/// answer and the product could not.
///
/// Against a real mongod, because all of this is aggregation pipelines: the grouping, the
/// date bucketing and the conditional sums are what is being tested, and an in-memory fake
/// would be testing the fake.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class AdminInsightsTests
{
    private readonly RadarDatabase _db;
    private readonly AdminInsightsService _insights;

    public AdminInsightsTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("admin-insights");
        _insights = new AdminInsightsService(_db);
    }

    private async Task Seen(string userId, AnalyticsEventType type, string? contentItemId = null, int daysAgo = 0)
    {
        await _db.AnalyticsEvents.InsertOneAsync(new AnalyticsEvent
        {
            UserId = userId,
            Type = type,
            ContentItemId = contentItemId,
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
        });
    }

    private async Task Published(string id, string title, string source, int daysAgo = 1)
    {
        await _db.ContentItems.InsertOneAsync(new ContentItem
        {
            Id = id,
            Title = title,
            Source = source,
            Type = ContentType.Article,
            PublishedAt = DateTime.UtcNow.AddDays(-daysAgo),
        });
    }

    // ── activity ─────────────────────────────────────────────────────────────

    /// <summary>
    /// People, not events.
    /// </summary>
    /// <remarks>
    /// One person opening forty things is one engaged reader and forty events. A chart that
    /// plots only events cannot tell that from forty readers, which is the difference between a
    /// product working and one person refreshing.
    /// </remarks>
    [Fact]
    public async Task ADayCountsPeopleSeparatelyFromEvents()
    {
        for (var i = 0; i < 5; i++) await Seen("ada", AnalyticsEventType.Open, $"item-{i}");
        await Seen("bola", AnalyticsEventType.Open, "item-9");

        var days = await _insights.ActivityByDayAsync();
        var today = days.Single();

        Assert.Equal(6, today.Events);
        Assert.Equal(2, today.People);
    }

    /// <summary>The window is a window: older events are not counted.</summary>
    [Fact]
    public async Task SomethingOlderThanTheWindowIsLeftOut()
    {
        await Seen("ada", AnalyticsEventType.Open, "recent", daysAgo: 2);
        await Seen("ada", AnalyticsEventType.Open, "ancient", daysAgo: 90);

        var days = await _insights.ActivityByDayAsync(days: 7);

        Assert.Single(days);
        Assert.Equal(1, days[0].Events);
    }

    /// <summary>
    /// The mix is named, not numbered.
    /// </summary>
    /// <remarks>
    /// Type persists as an Int32, so the stored value is the enum's position. A dashboard row
    /// reading "7" helps nobody.
    /// </remarks>
    [Fact]
    public async Task TheMixComesBackWithNamesRatherThanEnumNumbers()
    {
        await Seen("ada", AnalyticsEventType.Open, "a");
        await Seen("ada", AnalyticsEventType.Save, "a");
        await Seen("bola", AnalyticsEventType.Open, "b");

        var mix = await _insights.EventMixAsync();

        Assert.Equal(2, mix["Open"]);
        Assert.Equal(1, mix["Save"]);
    }

    // ── the funnel ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFunnelCountsEachStageFromItsOwnEvents()
    {
        await Seen("ada", AnalyticsEventType.Impression, "a");
        await Seen("ada", AnalyticsEventType.Impression, "b");
        await Seen("ada", AnalyticsEventType.Open, "a");
        await Seen("ada", AnalyticsEventType.Save, "a");
        await Seen("bola", AnalyticsEventType.NotRelevant, "b");

        var funnel = await _insights.FunnelAsync();

        Assert.Equal(2, funnel.Impressions);
        Assert.Equal(1, funnel.Opens);
        Assert.Equal(1, funnel.Saves);
        Assert.Equal(1, funnel.Dismissals);
        Assert.Equal(0, funnel.Reports);
    }

    /// <summary>
    /// A stage may exceed the one above it, and the numbers say so.
    /// </summary>
    /// <remarks>
    /// Counted from stored events rather than derived from each other. More opens than
    /// impressions is not an error to hide - it means impressions are not being logged where
    /// opens are, and smoothing it over would turn a gap in the instrumentation into a
    /// plausible-looking conversion rate.
    /// </remarks>
    [Fact]
    public async Task MoreOpensThanImpressionsIsReportedRatherThanSmoothed()
    {
        await Seen("ada", AnalyticsEventType.Open, "a");
        await Seen("ada", AnalyticsEventType.Open, "b");

        var funnel = await _insights.FunnelAsync();

        Assert.Equal(0, funnel.Impressions);
        Assert.Equal(2, funnel.Opens);
    }

    // ── what they engage with ────────────────────────────────────────────────

    /// <summary>A row says what the thing was. An id is not a finding.</summary>
    [Fact]
    public async Task EngagedItemsCarryTheirTitleAndSource()
    {
        await Published("popular", "Nurses win pay review", "Premium Times");
        await Published("quiet", "A quieter story", "The Cable");

        await Seen("ada", AnalyticsEventType.Open, "popular");
        await Seen("bola", AnalyticsEventType.Open, "popular");
        await Seen("ada", AnalyticsEventType.Save, "popular");
        await Seen("ada", AnalyticsEventType.Open, "quiet");

        var engaged = await _insights.MostEngagedAsync();

        Assert.Equal("popular", engaged[0].ContentItemId);
        Assert.Equal("Nurses win pay review", engaged[0].Title);
        Assert.Equal("Premium Times", engaged[0].Source);
        Assert.Equal(2, engaged[0].Opens);
        Assert.Equal(1, engaged[0].Saves);
    }

    /// <summary>
    /// An item that has since been deleted is dropped, not shown blank.
    /// </summary>
    /// <remarks>
    /// A blank row reads as a bug in the dashboard rather than a deletion in the corpus, and
    /// somebody would spend an afternoon on it.
    /// </remarks>
    [Fact]
    public async Task EventsForADeletedItemDoNotBecomeABlankRow()
    {
        await Seen("ada", AnalyticsEventType.Open, "deleted-since");

        Assert.Empty(await _insights.MostEngagedAsync());
    }

    /// <summary>
    /// Volume and attention side by side.
    /// </summary>
    /// <remarks>
    /// A source filing forty items a day for three opens is costing ingestion budget and feed
    /// space to nobody's benefit, and that is invisible on any chart ranking by opens alone.
    /// </remarks>
    [Fact]
    public async Task ASourceIsReportedWithWhatItPublishedAsWellAsWhatWasOpened()
    {
        await Published("a", "One", "Noisy Wire");
        await Published("b", "Two", "Noisy Wire");
        await Published("c", "Three", "Noisy Wire");
        await Published("d", "Four", "Quiet Journal");

        await Seen("ada", AnalyticsEventType.Open, "a");
        await Seen("ada", AnalyticsEventType.NotRelevant, "b");
        await Seen("bola", AnalyticsEventType.Open, "d");

        var sources = await _insights.BySourceAsync();
        var noisy = sources.Single(s => s.Source == "Noisy Wire");

        Assert.Equal(3, noisy.Published);
        Assert.Equal(1, noisy.Opens);
        Assert.Equal(1, noisy.Dismissals);
    }

    /// <summary>Nothing recorded yet is an empty answer, not a crash and not a zero-filled chart.</summary>
    [Fact]
    public async Task AnEmptyDatabaseAnswersEmpty()
    {
        Assert.Empty(await _insights.ActivityByDayAsync());
        Assert.Empty(await _insights.EventMixAsync());
        Assert.Empty(await _insights.MostEngagedAsync());
        Assert.Empty(await _insights.BySourceAsync());

        var funnel = await _insights.FunnelAsync();
        Assert.Equal(0, funnel.Opens);
    }
}

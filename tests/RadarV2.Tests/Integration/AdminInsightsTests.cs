using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Reading the events Radar has always recorded and never looked at.
/// </summary>
/// <remarks>
/// Seventeen kinds of event were being written - impressions, opens, saves, dismissals, reports -
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

    // ── what review found ────────────────────────────────────────────────────

    /// <summary>
    /// A purged item costs a row of the corpus, not a row of the answer.
    /// </summary>
    /// <remarks>
    /// The join used to run after the limit, so the three most-opened ids being gone took three
    /// of the twenty rows with them - and with a small limit it emptied the panel completely,
    /// under the caption "nothing has been opened in the last 30 days", while thirty opens sat
    /// inside the window. Ingestion purges duplicate urls on every cycle, so this is the
    /// ordinary case and not a corner.
    ///
    /// The earlier test asserted the nothing-but-deleted case, which is exactly why this one
    /// slipped through: zero live rows out of zero looks the same whichever order the stages run
    /// in.
    /// </remarks>
    [Fact]
    public async Task DeletedItemsDoNotEatTheRowsOfItemsThatStillExist()
    {
        foreach (var i in Enumerable.Range(1, 3))
            for (var open = 0; open < 10; open++)
                await Seen($"reader-{open}", AnalyticsEventType.Open, $"purged-{i}");

        await Published("alive-1", "Still here", "Premium Times");
        await Published("alive-2", "Also here", "The Cable");
        await Seen("ada", AnalyticsEventType.Open, "alive-1");
        await Seen("bola", AnalyticsEventType.Open, "alive-2");

        var engaged = await _insights.MostEngagedAsync(limit: 3);

        Assert.Equal(["alive-1", "alive-2"], engaged.Select(item => item.ContentItemId).Order());
    }

    /// <summary>
    /// The one case this screen exists to expose.
    /// </summary>
    /// <remarks>
    /// A source filing all day and earning nothing is the thing the published column is for, and
    /// building the rows out of the engaged events meant that source never appeared at all. The
    /// chart documented the problem and then hid it.
    /// </remarks>
    [Fact]
    public async Task ASourceThatPublishesAndEarnsNothingIsStillOnTheChart()
    {
        foreach (var i in Enumerable.Range(1, 12))
            await Published($"noise-{i}", $"Filler {i}", "Noisy Wire");

        await Published("good", "Worth reading", "Quiet Journal");
        await Seen("ada", AnalyticsEventType.Open, "good");

        var sources = await _insights.BySourceAsync();

        var noisy = sources.Single(source => source.Source == "Noisy Wire");
        Assert.Equal(12, noisy.Published);
        Assert.Equal(0, noisy.Opens);

        // And it is first, because twelve items of feed space is the larger footprint.
        Assert.Equal("Noisy Wire", sources[0].Source);
        Assert.Equal("Quiet Journal", sources[1].Source);
    }

    /// <summary>A source read constantly without filing anything new is not cut either.</summary>
    [Fact]
    public async Task ASourceWithNothingNewButPlentyOfReadersStaysOnTheChart()
    {
        await Published("old-favourite", "From last year", "Archive Weekly", daysAgo: 200);
        for (var i = 0; i < 9; i++) await Seen($"reader-{i}", AnalyticsEventType.Open, "old-favourite");

        foreach (var i in Enumerable.Range(1, 4))
            await Published($"new-{i}", $"Fresh {i}", "Daily Filler");

        var sources = await _insights.BySourceAsync();

        var archive = sources.Single(source => source.Source == "Archive Weekly");
        Assert.Equal(0, archive.Published);
        Assert.Equal(9, archive.Opens);
        Assert.Equal("Archive Weekly", sources[0].Source);
    }

    /// <summary>
    /// The funnel is one population, so an uneven stage really does mean a logging gap.
    /// </summary>
    /// <remarks>
    /// Opens are recorded for opportunities too, and those carry an opportunity id and no
    /// content id. Counting them beside feed impressions made Opens exceed Impressions by
    /// design, under a caption telling the reader that impressions are not being recorded
    /// everywhere opens are - sending somebody after a bug that was not there.
    /// </remarks>
    [Fact]
    public async Task TheFunnelCountsFeedContentAndNotOpportunityOpens()
    {
        await Seen("ada", AnalyticsEventType.Impression, "a");
        await Seen("ada", AnalyticsEventType.Open, "a");

        // An opportunity open: no content id on the event at all.
        await Seen("ada", AnalyticsEventType.Open);
        await Seen("bola", AnalyticsEventType.Open);

        var funnel = await _insights.FunnelAsync();

        Assert.Equal(1, funnel.Impressions);
        Assert.Equal(1, funnel.Opens);

        // Not lost - the mix still counts every event of every kind.
        Assert.Equal(3, (await _insights.EventMixAsync())["Open"]);
    }

    /// <summary>
    /// The window a caller is told about is the window that was used.
    /// </summary>
    /// <remarks>
    /// The clamp was silent and the request was echoed, so asking for 10000 days returned 180
    /// days of data under "nothing recorded in the last 10000 days" - a sentence nothing
    /// produced, rendered verbatim on the screen.
    /// </remarks>
    [Theory]
    [InlineData(10000, 180)]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(30, 30)]
    public void TheWindowReportedIsTheWindowUsed(int asked, int used) =>
        Assert.Equal(used, AdminInsightsService.WindowFor(asked));

    /// <summary>People are counted once per day, not once per visit.</summary>
    [Fact]
    public async Task PeopleAreCountedPerDayRatherThanAcrossTheWholeWindow()
    {
        await Seen("ada", AnalyticsEventType.Open, "a", daysAgo: 0);
        await Seen("ada", AnalyticsEventType.Open, "b", daysAgo: 0);
        await Seen("ada", AnalyticsEventType.Open, "c", daysAgo: 3);
        await Seen("bola", AnalyticsEventType.Open, "d", daysAgo: 3);

        var days = await _insights.ActivityByDayAsync();

        Assert.Equal(2, days.Count);
        Assert.Equal(2, days[0].People);
        Assert.Equal(2, days[0].Events);
        Assert.Equal(1, days[1].People);
        Assert.Equal(2, days[1].Events);
    }
}

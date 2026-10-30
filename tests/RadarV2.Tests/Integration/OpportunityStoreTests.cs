using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Tests.Integration;

/// <summary>
/// The window every opportunity screen is served from.
/// </summary>
/// <remarks>
/// Scoring runs over the five hundred most recently discovered opportunities, and which five
/// hundred those are was decided by a query nothing could test. It had no sort at all, so the
/// server returned them in storage order: the window filled with the oldest rows, and everything
/// ingested afterwards was never scored and never shown. Most job postings carry no deadline, so
/// they never expired out of it either - the feed would have frozen, silently, looking exactly
/// like a quiet week.
///
/// A real database, because the defect was in what Mongo does when you do not tell it what you
/// want, which no unit test can observe.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class OpportunityStoreTests
{
    private readonly RadarDatabase _db;

    public OpportunityStoreTests(RadarDatabaseFixture fixture) => _db = fixture.Database("opportunities");

    /// <summary>The window, as the service takes it.</summary>
    private const int Window = 500;

    private async Task Store(string title, DateTime discovered, DateTime? deadline = null)
    {
        await _db.Opportunities.InsertOneAsync(new Opportunity
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Organisation = "Somebody",
            Url = $"https://example.org/{Guid.NewGuid():N}",
            DiscoveredAt = discovered,
            Deadline = deadline,
        });
    }

    private async Task<List<Opportunity>> Window500()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var notExpired = Builders<Opportunity>.Filter.Or(
            Builders<Opportunity>.Filter.Eq(o => o.Deadline, null),
            Builders<Opportunity>.Filter.Gte(o => o.Deadline, cutoff));

        return await _db.Opportunities
            .Find(notExpired)
            .SortByDescending(o => o.DiscoveredAt)
            .Limit(Window)
            .ToListAsync();
    }

    /// <summary>
    /// A newly ingested opportunity is reachable once the collection is larger than the window.
    /// </summary>
    /// <remarks>
    /// The exact shape adding a 595-entry source created: insert more than the window holds, with
    /// no deadlines, and then ask whether anything new can still be seen.
    /// </remarks>
    [Fact]
    public async Task SomethingIngestedTodayIsInTheWindow()
    {
        var old = DateTime.UtcNow.AddDays(-30);

        for (var i = 0; i < Window + 120; i++)
        {
            await Store($"An old posting {i}", old.AddMinutes(i));
        }

        await Store("Today's scholarship", DateTime.UtcNow);

        var window = await Window500();

        Assert.Equal(Window, window.Count);
        Assert.Contains(window, o => o.Title == "Today's scholarship");
    }

    /// <summary>And the window is the newest, not an arbitrary five hundred.</summary>
    [Fact]
    public async Task TheWindowIsTheMostRecentlyDiscovered()
    {
        for (var i = 0; i < 20; i++)
        {
            await Store($"Posting {i}", DateTime.UtcNow.AddDays(-i));
        }

        var window = await _db.Opportunities
            .Find(Builders<Opportunity>.Filter.Empty)
            .SortByDescending(o => o.DiscoveredAt)
            .Limit(5)
            .ToListAsync();

        Assert.Equal(
            ["Posting 0", "Posting 1", "Posting 2", "Posting 3", "Posting 4"],
            window.Select(o => o.Title));
    }

    /// <summary>Something that closed yesterday is out of it.</summary>
    [Fact]
    public async Task AnExpiredOpportunityIsNotScored()
    {
        await Store("Closed last month", DateTime.UtcNow, deadline: DateTime.UtcNow.AddDays(-30));
        await Store("Open", DateTime.UtcNow, deadline: DateTime.UtcNow.AddDays(14));
        await Store("No deadline given", DateTime.UtcNow);

        var titles = (await Window500()).Select(o => o.Title).ToList();

        Assert.DoesNotContain("Closed last month", titles);
        Assert.Contains("Open", titles);
        Assert.Contains("No deadline given", titles);
    }

    /// <summary>
    /// The link is the identity, and the database is what enforces it.
    /// </summary>
    /// <remarks>
    /// Ingestion checks for the link before inserting, which is a read followed by a write - two
    /// runs overlapping, or one feed republishing a post across pages, and the check passes
    /// twice. The unique index is the part that cannot be raced.
    /// </remarks>
    [Fact]
    public async Task TheSameLinkCannotBeStoredTwice()
    {
        await _db.Opportunities.Indexes.CreateOneAsync(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Ascending(o => o.Url),
                new CreateIndexOptions { Unique = true, Sparse = true }));

        var link = "https://example.org/the-same-posting";

        async Task Insert() => await _db.Opportunities.InsertOneAsync(new Opportunity
        {
            Id = Guid.NewGuid().ToString(),
            Title = "A posting",
            Url = link,
            DiscoveredAt = DateTime.UtcNow,
        });

        await Insert();

        await Assert.ThrowsAsync<MongoWriteException>(Insert);
        Assert.Equal(1, await _db.Opportunities.CountDocumentsAsync(o => o.Url == link));
    }
}

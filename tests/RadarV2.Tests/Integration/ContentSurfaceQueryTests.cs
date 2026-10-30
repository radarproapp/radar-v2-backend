using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// The two surfaces, asked of a real database.
/// </summary>
/// <remarks>
/// ContentSurfaceTests asserts on the predicates and on the text of the rendered queries. Neither
/// catches the thing that actually went wrong twice: a query and a predicate that disagree. The
/// first time, $nin matched a document with no Layer field while the model's default said the
/// opposite; the second, both queries excluded it and the predicate claimed it, so a legacy
/// article was reachable from no screen at all.
///
/// A document with a field missing cannot be written through the model - the model always has the
/// field - so these insert raw BSON, which is what production actually holds.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class ContentSurfaceQueryTests
{
    private readonly RadarDatabase _db;

    public ContentSurfaceQueryTests(RadarDatabaseFixture fixture) =>
        _db = fixture.Database("surface");

    private async Task<ContentItem> Stored(ContentType type, ContentLayer layer)
    {
        var item = new ContentItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = $"{type} in {layer}",
            Type = type,
            Layer = layer,
        };

        await _db.ContentItems.InsertOneAsync(item);
        return item;
    }

    /// <summary>A document as it is really stored, with a field that was never written.</summary>
    private async Task<string> StoredWithoutLayer(ContentType type)
    {
        var id = Guid.NewGuid().ToString();

        await _db.ContentItems.Database
            .GetCollection<BsonDocument>("content_items")
            .InsertOneAsync(new BsonDocument
            {
                ["_id"] = id,
                ["Title"] = "An article from before layers existed",
                ["Type"] = (int)type,
            });

        return id;
    }

    private async Task<List<string>> Matching(FilterDefinition<ContentItem> filter) =>
        [.. (await _db.ContentItems.Find(filter).ToListAsync()).Select(i => i.Id)];

    /// <summary>News goes to the feed and a paper does not.</summary>
    [Fact]
    public async Task TheFeedCarriesNewsAndNotPapers()
    {
        var news = await Stored(ContentType.Article, ContentLayer.Finance);
        var paper = await Stored(ContentType.ResearchPaper, ContentLayer.Academic);

        var feed = await Matching(ContentSurface.IntelligenceFilter());

        Assert.Contains(news.Id, feed);
        Assert.DoesNotContain(paper.Id, feed);
    }

    /// <summary>
    /// A think tank's analysis is research even when its feed calls it an article.
    /// </summary>
    [Fact]
    public async Task AnArticleFromAPolicySourceIsResearch()
    {
        var working = await Stored(ContentType.Article, ContentLayer.Policy);

        Assert.DoesNotContain(working.Id, await Matching(ContentSurface.IntelligenceFilter()));
        Assert.Contains(working.Id, await Matching(ContentSurface.ResearchFilter()));
    }

    /// <summary>
    /// Every stored document is claimed by exactly one surface, including the ones missing a field.
    /// </summary>
    /// <remarks>
    /// The invariant the whole class exists for, asked of the database rather than of a
    /// predicate. A document claimed by neither is ingested, stored, ranked and shown to nobody,
    /// and that is invisible from both screens because each looks correct on its own terms.
    /// </remarks>
    [Fact]
    public async Task EveryStoredDocumentBelongsToOneSurface()
    {
        var written = new List<string>();

        foreach (var type in Enum.GetValues<ContentType>())
        {
            if (type is ContentType.Video) continue;

            foreach (var layer in new[]
                     {
                         ContentLayer.Policy, ContentLayer.Academic, ContentLayer.Ideas,
                         ContentLayer.Finance, ContentLayer.Skills, ContentLayer.Sports,
                     })
            {
                written.Add((await Stored(type, layer)).Id);
            }
        }

        // And the shape no fixture ever has: a field that was never written.
        written.Add(await StoredWithoutLayer(ContentType.Article));
        written.Add(await StoredWithoutLayer(ContentType.Podcast));

        var feed = await Matching(ContentSurface.IntelligenceFilter());
        var research = await Matching(ContentSurface.ResearchFilter());

        foreach (var id in written)
        {
            var surfaces = (feed.Contains(id) ? 1 : 0) + (research.Contains(id) ? 1 : 0);
            Assert.True(surfaces == 1, $"{id} is on {surfaces} surfaces.");
        }
    }

    /// <summary>
    /// And the query agrees with the predicate, document by document.
    /// </summary>
    /// <remarks>
    /// This is the comparison nothing was making. Both halves looked right in isolation; they
    /// disagreed about the one document neither author had in mind.
    /// </remarks>
    [Fact]
    public async Task TheQueryAndThePredicateAgree()
    {
        await Stored(ContentType.Article, ContentLayer.Policy);
        await Stored(ContentType.Article, ContentLayer.Finance);
        await Stored(ContentType.Essay, ContentLayer.Ideas);
        await Stored(ContentType.Podcast, ContentLayer.Skills);
        await StoredWithoutLayer(ContentType.Article);

        var everything = await _db.ContentItems.Find(_ => true).ToListAsync();
        var feed = await Matching(ContentSurface.IntelligenceFilter());
        var research = await Matching(ContentSurface.ResearchFilter());

        foreach (var item in everything)
        {
            Assert.Equal(ContentSurface.IsIntelligence(item), feed.Contains(item.Id));
            Assert.Equal(ContentSurface.IsResearch(item), research.Contains(item.Id));
        }
    }
}

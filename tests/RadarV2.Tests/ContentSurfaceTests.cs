using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Which of the two products a piece of content belongs to.
/// </summary>
/// <remarks>
/// Radar Intelligence is what is happening; Research Discovery is what has been established. They
/// were one list, so a working paper from a think tank arrived between two news items, and
/// anybody looking for the working paper had to scroll a news feed to find it.
/// </remarks>
public class ContentSurfaceTests
{
    private static ContentItem Item(ContentType type, ContentLayer layer = ContentLayer.Finance) =>
        new() { Id = "c1", Title = "Something", Type = type, Layer = layer };

    [Theory]
    [InlineData(ContentType.Article)]
    [InlineData(ContentType.Podcast)]
    public void NewsAndPodcastsAreTheFeed(ContentType type)
    {
        Assert.True(ContentSurface.IsIntelligence(Item(type)));
        Assert.False(ContentSurface.IsResearch(Item(type)));
    }

    [Theory]
    [InlineData(ContentType.ResearchPaper)]
    [InlineData(ContentType.PolicyPaper)]
    [InlineData(ContentType.Essay)]
    public void PapersPolicyAndIdeasAreResearch(ContentType type)
    {
        Assert.True(ContentSurface.IsResearch(Item(type)));
        Assert.False(ContentSurface.IsIntelligence(Item(type)));
    }

    /// <summary>
    /// A think tank's analysis is research even when its feed calls it an article.
    /// </summary>
    /// <remarks>
    /// 161 of the registered sources default to Article, including every policy institute and
    /// university, because that is what their RSS says. The layer is the classification somebody
    /// made on purpose, so it is the one that decides.
    /// </remarks>
    [Theory]
    [InlineData(ContentLayer.Policy)]
    [InlineData(ContentLayer.Academic)]
    [InlineData(ContentLayer.Ideas)]
    public void AnArticleFromAResearchSourceIsResearch(ContentLayer layer)
    {
        var item = Item(ContentType.Article, layer);

        Assert.True(ContentSurface.IsResearch(item));
        Assert.False(ContentSurface.IsIntelligence(item));
    }

    /// <summary>And a news article from a news source stays in the feed.</summary>
    [Theory]
    [InlineData(ContentLayer.Finance)]
    [InlineData(ContentLayer.Sports)]
    [InlineData(ContentLayer.Career)]
    [InlineData(ContentLayer.Skills)]
    public void AnArticleFromANewsSourceIsTheFeed(ContentLayer layer)
    {
        Assert.True(ContentSurface.IsIntelligence(Item(ContentType.Article, layer)));
    }

    /// <summary>
    /// Nothing written falls between the two.
    /// </summary>
    /// <remarks>
    /// The failure that cannot be seen from either screen: the feed stops carrying a type and
    /// Research Discovery never starts, so the content is ingested, stored, ranked and shown to
    /// nobody. Every type except video has to land on exactly one surface.
    /// </remarks>
    [Fact]
    public void EveryWrittenTypeLandsOnExactlyOneSurface()
    {
        foreach (var type in Enum.GetValues<ContentType>())
        {
            if (type is ContentType.Video) continue;

            foreach (var layer in Enum.GetValues<ContentLayer>())
            {
                var item = Item(type, layer);
                var surfaces = (ContentSurface.IsIntelligence(item) ? 1 : 0)
                             + (ContentSurface.IsResearch(item) ? 1 : 0);

                Assert.True(surfaces == 1, $"{type} in {layer} appears on {surfaces} surfaces.");
            }
        }
    }

    /// <summary>
    /// Video is on neither, deliberately.
    /// </summary>
    /// <remarks>
    /// It is not news, and a lesson is where somebody watches something: Radar Learn asks for
    /// videos by subject rather than showing whatever arrived today. Asserted so that the gap is
    /// a decision rather than an oversight the test above would otherwise have caught.
    /// </remarks>
    [Fact]
    public void VideoIsOnNeitherSurface()
    {
        var video = Item(ContentType.Video);

        Assert.False(ContentSurface.IsIntelligence(video));
        Assert.False(ContentSurface.IsResearch(video));
    }

    // ── The queries that actually run ───────────────────────────────────────

    /// <summary>
    /// The query and the predicate agree about a document missing its layer.
    /// </summary>
    /// <remarks>
    /// Production holds documents that predate fields — the suite asserts that elsewhere, and
    /// IgnoreExtraElements is registered for exactly that reason. The two halves of this split
    /// disagreed on one: Mongo's $nin matches a missing field, so the query called a layerless
    /// article feed content, while the model defaults Layer to Ideas, so the predicate called the
    /// same document research. Inverted, on the one case nobody writes a fixture for.
    ///
    /// Fixed by giving the feed filter the same shape as the predicate: an explicit list of the
    /// layers that are not research, so a missing layer matches neither and the document cannot
    /// be claimed by a query that the predicate would deny.
    /// </remarks>
    [Fact]
    public void AMissingLayerIsNotClaimedByAQueryThePredicateWouldDeny()
    {
        var rendered = Render(ContentSurface.IntelligenceFilter());

        // $nin matches a document with no Layer at all; $in does not.
        Assert.DoesNotContain("$nin", rendered);
    }

    private static string Render(MongoDB.Driver.FilterDefinition<ContentItem> filter)
    {
        var registry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
        return filter
            .Render(new MongoDB.Driver.RenderArgs<ContentItem>(registry.GetSerializer<ContentItem>(), registry))
            .ToString();
    }

    /// <summary>
    /// Both queries name a type and a layer, so neither can admit a document by omission.
    /// </summary>
    [Fact]
    public void BothQueriesAreExplicitAboutWhatTheyAdmit()
    {
        foreach (var rendered in new[]
                 {
                     Render(ContentSurface.IntelligenceFilter()),
                     Render(ContentSurface.ResearchFilter()),
                 })
        {
            Assert.Contains("Type", rendered);
            Assert.Contains("Layer", rendered);
        }
    }

    /// <summary>
    /// A document stored before the layer field existed is still somebody's to show.
    /// </summary>
    /// <remarks>
    /// Mongo's $in does not match an absent field, and the model defaults Layer to Ideas - so a
    /// legacy article read as research once loaded, while neither query claimed it. Ingested,
    /// stored, and reachable from no screen at all, which is the one failure this class exists
    /// to prevent.
    /// </remarks>
    [Fact]
    public void ADocumentWithNoLayerIsClaimedByResearch()
    {
        var rendered = Render(ContentSurface.ResearchFilter());

        // The query says what the predicate says: no layer means the default, which is research.
        Assert.Contains("$exists", rendered);
    }

    /// <summary>
    /// Narrowing by type does not reach past the surface.
    /// </summary>
    /// <remarks>
    /// Intelligence is two conditions - a news type AND not a research layer - and asking for a
    /// type replaced both of them. So /api/feed?type=Article handed back a think tank's working
    /// paper under a heading that says Intelligence: the behaviour the split exists to end,
    /// surviving one layer below where it was first closed.
    ///
    /// Checked against the source because the filter is assembled inside a private method with
    /// no host to query here, and the shape of the composition is the thing that was wrong.
    /// </remarks>
    [Fact]
    public void AskingForATypeKeepsTheSurface()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var source = File.ReadAllText(
            Path.Combine(dir!.FullName, "Services", "MongoIntelligenceFeedService.cs"));

        // The surface first, then the type narrowed within it - never the type instead of it.
        Assert.Contains("var filter = surface ?? ContentSurface.IntelligenceFilter();", source);
        Assert.Contains("filter &= Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value)", source);
    }
}

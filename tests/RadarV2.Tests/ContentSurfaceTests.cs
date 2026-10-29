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
}

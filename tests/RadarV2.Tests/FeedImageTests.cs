using System.Xml.Linq;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// The picture the publisher put in the body, which nothing was looking for.
/// </summary>
/// <remarks>
/// Measured across 918 items from 58 live feeds before this existed: 34% carried a picture
/// through one of the three structured routes a feed can declare one, and another 33% had an
/// image sitting in the description HTML that nothing read. InfoQ had none of fifteen and fifteen
/// in the body; Simon Willison none of thirty and twelve; BusinessDay Nigeria none of five and
/// all five.
///
/// Worth reaching for because the feed is a scrolling wall of cards. One card falling back to a
/// layer gradient is deliberate; two thirds of a feed doing it is just grey.
///
/// The risk is the opposite of a missing picture: a body is full of images that are not the
/// article, and a wall of identical one-pixel tracking gifs is worse than a wall of gradients.
/// Most of what follows is about refusing those.
/// </remarks>
public class FeedImageTests
{
    private static XElement Entry(string body) =>
        XElement.Parse($"<item><description>{System.Security.SecurityElement.Escape(body)}</description></item>");

    private static string? Read(string body) => RssIngestionService.FirstImageInBody(Entry(body));

    [Fact]
    public void ThePictureInTheBodyIsFound() =>
        Assert.Equal(
            "https://example.com/lead.jpg",
            Read("<p>Intro</p><img src=\"https://example.com/lead.jpg\" alt=\"\"><p>More</p>"));

    [Theory]
    [InlineData("<img src='https://example.com/a.png'>")]
    [InlineData("<img class=\"hero\" src = \"https://example.com/a.png\" width=\"600\">")]
    [InlineData("<IMG SRC=\"https://example.com/a.png\">")]
    public void TheAttributeIsReadHoweverItIsWritten(string body) =>
        Assert.Equal("https://example.com/a.png", Read(body));

    /// <summary>
    /// The first content image wins, which is almost always the lead.
    /// </summary>
    /// <remarks>
    /// Collecting them all and choosing would need a rule nobody has — size is usually absent
    /// from the markup — and the first image in an article body is the one the card wants.
    /// </remarks>
    [Fact]
    public void TheFirstRealImageWins() =>
        Assert.Equal(
            "https://example.com/first.jpg",
            Read("<img src=\"https://example.com/first.jpg\"><img src=\"https://example.com/second.jpg\">"));

    /// <summary>
    /// Furniture is skipped, and the real picture after it is still found.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is specific and ugly: taking the first img
    /// indiscriminately gives a feed of identical one-pixel gifs, because a tracking pixel is
    /// usually the first thing in the body. Matched on the URL because the markup rarely says -
    /// width and height are absent more often than present, and a pixel is styled rather than
    /// sized.
    /// </remarks>
    [Theory]
    [InlineData("https://feeds.feedburner.com/~r/site/~4/abc")]
    [InlineData("https://example.com/pixel.gif")]
    [InlineData("https://example.com/img/1x1.png")]
    [InlineData("https://example.com/spacer.gif")]
    [InlineData("https://www.gravatar.com/avatar/abc")]
    [InlineData("https://example.com/share-button.png")]
    [InlineData("https://example.com/assets/logo.svg")]
    [InlineData("https://example.com/social-icon.png")]
    [InlineData("https://stats.example.com/track?id=1")]
    public void FurnitureIsSkippedAndTheRealPictureIsStillFound(string junk)
    {
        var found = Read($"<img src=\"{junk}\"><img src=\"https://example.com/lead.jpg\">");

        Assert.Equal("https://example.com/lead.jpg", found);
    }

    [Fact]
    public void AnEntryOfNothingButFurnitureHasNoPicture() =>
        Assert.Null(Read("<img src=\"https://example.com/pixel.gif\"><img src=\"https://x.com/logo.png\">"));

    /// <summary>
    /// A relative path is not usable and is not guessed at.
    /// </summary>
    /// <remarks>
    /// The card renders from another origin, so a relative src resolves against Radar rather than
    /// the publisher. Inferring the base from the feed URL is how you end up hotlinking the wrong
    /// site's image, which is worse than showing a gradient.
    /// </remarks>
    [Theory]
    [InlineData("/images/lead.jpg")]
    [InlineData("images/lead.jpg")]
    [InlineData("//cdn.example.com/lead.jpg")]
    public void ARelativePathIsNotUsed(string src) =>
        Assert.Null(Read($"<img src=\"{src}\">"));

    [Fact]
    public void AnEntityEncodedUrlIsDecoded() =>
        Assert.Equal(
            "https://example.com/a.jpg?w=600&h=400",
            Read("<img src=\"https://example.com/a.jpg?w=600&amp;h=400\">"));

    [Theory]
    [InlineData("")]
    [InlineData("<p>No pictures here at all.</p>")]
    [InlineData("<img alt=\"no src\">")]
    public void AnEntryWithNoPictureHasNone(string body) => Assert.Null(Read(body));

    /// <summary>The body is read wherever the feed happens to put it.</summary>
    /// <remarks>
    /// RSS uses description, WordPress uses content:encoded, Atom uses summary or content, and
    /// plenty of feeds use more than one. Reading only description would have missed a large
    /// share of exactly the feeds this exists to fix.
    /// </remarks>
    [Theory]
    [InlineData("description")]
    [InlineData("summary")]
    [InlineData("content")]
    public void TheBodyIsFoundWhicheverElementCarriesIt(string element)
    {
        var entry = XElement.Parse(
            $"<item><{element}>&lt;img src=\"https://example.com/a.jpg\"&gt;</{element}></item>");

        Assert.Equal("https://example.com/a.jpg", RssIngestionService.FirstImageInBody(entry));
    }

    /// <summary>A body that is not markup does not throw.</summary>
    [Fact]
    public void APlainTextBodyIsNotAPicture() =>
        Assert.Null(Read("An ordinary summary with no markup, mentioning an img tag in passing."));
}

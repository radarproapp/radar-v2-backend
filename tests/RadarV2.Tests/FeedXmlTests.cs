using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// Feeds that are real and were being thrown away.
/// </summary>
/// <remarks>
/// An audit of all 250 content sources found 30 returning 200 with a body the XML reader refused,
/// and twenty of those thirty are current feeds: 99% Invisible with 842 episodes, Hard Fork with
/// 225, WHO with 83, three Conversation feeds with 50 each. Every reported reason was the same
/// shape - a declaration not at position zero - which is a byte-order mark or a blank line some
/// CMS emitted, and changes nothing about what the feed says.
/// </remarks>
public class FeedXmlTests
{
    private const string Feed =
        """<?xml version="1.0" encoding="utf-8"?><rss version="2.0"><channel><item><title>A</title></item></channel></rss>""";

    [Fact]
    public void AWellFormedFeedParsesExactlyAsBefore()
    {
        var document = FeedXml.Parse(Feed);

        Assert.NotNull(document);
        Assert.Equal("rss", document.Root!.Name.LocalName);
    }

    /// <summary>
    /// The preamble the strict reader refused, in each of the forms publishers actually send.
    /// </summary>
    /// <remarks>
    /// "Unexpected XML declaration. The XML declaration must be the first node in the document,
    /// and no whitespace characters are allowed to appear before it. Line 2, position 3." Twenty
    /// feeds, every night, for that.
    /// </remarks>
    [Theory]
    [InlineData("﻿")]
    [InlineData("\n")]
    [InlineData("\r\n\r\n")]
    [InlineData("  \n  ")]
    [InlineData("﻿\n ")]
    [InlineData("​")]
    public void ALeadingByteOrderMarkOrBlankLineNoLongerLosesTheFeed(string preamble)
    {
        var document = FeedXml.Parse(preamble + Feed);

        Assert.NotNull(document);
        Assert.Single(document.Descendants("item"));
    }

    [Fact]
    public void AFeedWithNoDeclarationStillParses()
    {
        var document = FeedXml.Parse("﻿<rss version=\"2.0\"><channel><item/></channel></rss>");

        Assert.NotNull(document);
        Assert.Equal("rss", document.Root!.Name.LocalName);
    }

    [Fact]
    public void AnAtomFeedBehindAPreambleParses()
    {
        var document = FeedXml.Parse(
            "\n<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><title>A</title></entry></feed>");

        Assert.NotNull(document);
        Assert.Equal("feed", document.Root!.Name.LocalName);
    }

    /// <summary>
    /// Lenient about the preamble is not lenient about the content.
    /// </summary>
    /// <remarks>
    /// Ten of the thirty genuinely are not feeds - a 404 page, a redirect landing, a site that
    /// stopped publishing one. Those must stay failures, or the audit that found this becomes
    /// impossible to run again: everything would look like it parsed.
    /// </remarks>
    [Theory]
    [InlineData("<html><body><h1>404 Not Found</h1></body></html>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SomethingThatIsNotAFeedIsStillNotAFeed(string? body) =>
        Assert.Null(FeedXml.Parse(body));

    /// <summary>
    /// A page that merely mentions a feed element deep inside it is not a feed.
    /// </summary>
    /// <remarks>
    /// The reason the search for an opening tag is bounded. Without a bound, an HTML page
    /// containing the string "&lt;rss" in a script or a link three kilobytes down would be
    /// sliced into something that parses, and the result is a document nobody meant to publish.
    /// </remarks>
    [Fact]
    public void AnHtmlPageMentioningAFeedTagFarInIsNotSalvaged()
    {
        var page = "<html><body>" + new string('x', 4000) + "<rss version=\"2.0\"><channel/></rss></body></html>";

        Assert.Null(FeedXml.Parse(page));
    }

    [Fact]
    public void AFeedIsNotTruncatedByTheRecovery()
    {
        var document = FeedXml.Parse(
            "﻿<?xml version=\"1.0\"?><rss><channel><item><title>A</title></item>"
            + "<item><title>B</title></item><item><title>C</title></item></channel></rss>");

        Assert.NotNull(document);
        Assert.Equal(3, document.Descendants("item").Count());
    }
}

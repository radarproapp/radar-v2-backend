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

    /// <summary>
    /// A feed whose root is spelt differently is still a feed.
    /// </summary>
    /// <remarks>
    /// The regression this check introduced and this test exists to stop. Comparing the root
    /// ordinally rejected &lt;RSS&gt; and &lt;Feed&gt;, both of which parsed and yielded items
    /// perfectly well before the check was added - so a commit written to stop losing sources
    /// invented a new way to lose them, and only for publishers on older software.
    /// </remarks>
    [Theory]
    [InlineData("<RSS version=\"2.0\"><channel><item><title>A</title></item></channel></RSS>")]
    [InlineData("<Rss version=\"2.0\"><channel><item><title>A</title></item></channel></Rss>")]
    [InlineData("<Feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><title>A</title></entry></Feed>")]
    [InlineData("<channel><item><title>A</title></item></channel>")]
    public void AFeedWhoseRootIsSpeltDifferentlyIsStillAFeed(string body) =>
        Assert.NotNull(FeedXml.Parse(body));

    /// <summary>
    /// A body that expands itself is refused rather than read.
    /// </summary>
    /// <remarks>
    /// Measured before this was closed: four hundred bytes, ten entities nested six deep, became
    /// a six-million character document and parsed successfully. Eight feeds are fetched at once
    /// from 250 URLs Radar does not control, and the ingestion client caps no response size, so
    /// one compromised publisher was enough to cost real memory on every cycle.
    ///
    /// Asserting null rather than asserting a time or a size: the point is that the document is
    /// never built at all.
    /// </remarks>
    [Fact]
    public void ABodyThatExpandsItselfIsRefused()
    {
        var bomb =
            "<?xml version=\"1.0\"?>"
            + "<!DOCTYPE rss [<!ENTITY a \"aaaaaaaaaa\">"
            + "<!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">"
            + "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">"
            + "<!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\">]>"
            + "<rss><channel><item><title>&d;</title></item></channel></rss>";

        Assert.Null(FeedXml.Parse(bomb));
    }

    /// <summary>
    /// A feed that merely declares a DTD is still read; one that depends on it is not.
    /// </summary>
    /// <remarks>
    /// Prohibiting entity processing rejects the whole document, but the slice-from-the-first-tag
    /// recovery then drops the declaration and parses what follows - so a harmless DTD costs
    /// nothing, which is better than the outright refusal this test first asserted.
    ///
    /// It stays safe because the two cases cannot be confused: once the declaration is sliced
    /// away, any entity the body actually references is undefined and the parse fails. A document
    /// is read only when the DTD made no difference to it.
    /// </remarks>
    [Fact]
    public void ADtdIsDroppedRatherThanObeyed()
    {
        var harmless = FeedXml.Parse(
            "<!DOCTYPE rss><rss><channel><item><title>A</title></item></channel></rss>");

        Assert.NotNull(harmless);
        Assert.Single(harmless.Descendants("item"));

        // Depends on an entity the declaration defined, so there is nothing to read once the
        // declaration is gone.
        Assert.Null(FeedXml.Parse(
            "<!DOCTYPE rss [<!ENTITY t \"A\">]><rss><channel><item><title>&t;</title></item></channel></rss>"));
    }

    /// <summary>
    /// The slice-from-the-first-tag branch, proven positively.
    /// </summary>
    /// <remarks>
    /// The behaviour the whole file leads with was previously exercised only by a test asserting
    /// null. These are the shapes that reach it: a PHP notice, an HTML comment, and a
    /// mis-decoded byte-order mark - all seen on real feeds, none recoverable by trimming alone.
    /// </remarks>
    [Theory]
    [InlineData("<br />\n<b>Notice</b>: Undefined index in /var/www/feed.php on line 3\n")]
    [InlineData("<!-- generated by a plugin -->\n")]
    [InlineData("ï»¿")]
    public void APreambleThatTrimmingCannotRemoveIsSlicedOff(string junk)
    {
        var document = FeedXml.Parse(junk + Feed);

        Assert.NotNull(document);
        Assert.Single(document.Descendants("item"));
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

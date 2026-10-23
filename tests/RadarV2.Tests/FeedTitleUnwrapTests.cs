using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// A headline must not arrive carrying its own markup.
///
/// Some publishers escape their own CDATA, so the title element holds
/// <c>&amp;lt;![CDATA[Real Title]]&amp;gt;</c>. XML decoding turns that into the literal
/// characters, which is a valid string as far as everything downstream can tell — so Pharmacy
/// Times headlines reached the feed reading "&lt;![CDATA[Kidney Week 2026: What to Know".
/// </summary>
public class FeedTitleUnwrapTests
{
    [Fact]
    public void RemovesACdataWrapperThatArrivedAsText()
    {
        Assert.Equal(
            "Kidney Week 2026: What to Know for Nephrology",
            RssIngestionService.Unwrap("<![CDATA[Kidney Week 2026: What to Know for Nephrology]]>"));
    }

    [Fact]
    public void TrimsAroundAndInsideTheWrapper()
    {
        Assert.Equal("Using Pharmacy School", RssIngestionService.Unwrap("  <![CDATA[ Using Pharmacy School ]]>  "));
    }

    // An ordinary headline must come through untouched, including one that merely mentions CDATA.
    [Theory]
    [InlineData("Rural Health Care: A Look at Priorities")]
    [InlineData("What CDATA sections are for")]
    [InlineData("")]
    public void LeavesOrdinaryTitlesAlone(string title)
    {
        Assert.Equal(title, RssIngestionService.Unwrap(title));
    }

    // Half a wrapper is not a wrapper; stripping it would corrupt a real headline.
    [Fact]
    public void IgnoresAnUnbalancedWrapper()
    {
        Assert.Equal("<![CDATA[Truncated headline", RssIngestionService.Unwrap("<![CDATA[Truncated headline"));
    }

    [Fact]
    public void HandlesNull()
    {
        Assert.Equal(string.Empty, RssIngestionService.Unwrap(null));
    }
}

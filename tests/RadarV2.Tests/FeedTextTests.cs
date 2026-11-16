using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// Turning what a source sends into something safe to put in front of a person.
/// </summary>
/// <remarks>
/// Shared because it was copied, and the copy had drifted: the RSS path cleaned its descriptions
/// and the TalentApply path did not. That left the one source Radar is actually permitted to store
/// descriptions from as the only one capable of rendering raw ATS markup at a reader.
/// </remarks>
public class FeedTextTests
{
    [Theory]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("Research &amp; development", "Research & development")]
    [InlineData("Line one\nLine two", "Line one Line two")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MarkupAndEntitiesAndWhitespaceAreCleaned(string? raw, string expected) =>
        Assert.Equal(expected, FeedText.Clean(raw));

    /// <summary>
    /// Collapsing runs of whitespace, however long.
    /// </summary>
    /// <remarks>
    /// The previous version did a single Replace("  ", " "), which halves a run rather than
    /// collapsing it: six spaces came out as three. Stripping tags turns indented HTML into
    /// exactly those long runs, so this was the common case rather than the odd one.
    /// </remarks>
    [Fact]
    public void LongRunsOfWhitespaceCollapseCompletely() =>
        Assert.Equal("a b", FeedText.Clean("a" + new string(' ', 40) + "b"));

    /// <summary>Decoding happens after stripping, so an entity cannot smuggle a tag back in.</summary>
    [Fact]
    public void AnEntityThatDecodesIntoATagDoesNotReintroduceMarkup() =>
        Assert.DoesNotContain("<script", FeedText.Clean("&lt;script&gt;alert(1)&lt;/script&gt;"),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TextShorterThanTheLimitIsUntouched() =>
        Assert.Equal("Data Analyst", FeedText.Shorten("Data Analyst", 160));

    [Fact]
    public void TextIsCutAtTheLimitWithAnEllipsis()
    {
        var shortened = FeedText.Shorten(new string('a', 200), 160);

        Assert.Equal(161, shortened.Length);
        Assert.EndsWith("…", shortened);
    }

    /// <summary>
    /// A cut never splits a character in half.
    /// </summary>
    /// <remarks>
    /// A substring cuts by UTF-16 unit rather than by character, so a title whose cut lands inside
    /// an emoji stores a lone surrogate - rendered as the replacement glyph, and invalid text to
    /// anything downstream that validates. Job titles carry emoji more often than one would hope.
    /// </remarks>
    [Fact]
    public void ACutDoesNotSplitASurrogatePair()
    {
        // The rocket sits exactly across the boundary: one unit inside, one outside.
        var title = new string('a', 9) + "🚀" + new string('b', 20);

        var shortened = FeedText.Shorten(title, 10);

        Assert.DoesNotContain(shortened, char.IsSurrogate);
        Assert.Equal("aaaaaaaaa…", shortened);
    }

    /// <summary>A pair that fits entirely inside the limit is kept whole.</summary>
    /// <remarks>
    /// The other side of the same rule: backing off is only correct when the cut would split a
    /// pair, not whenever one is near the boundary.
    /// </remarks>
    [Fact]
    public void ACutThatLandsCleanlyAfterAPairKeepsIt()
    {
        var title = new string('a', 8) + "🚀" + new string('b', 20);

        var shortened = FeedText.Shorten(title, 10);

        Assert.Equal("aaaaaaaa🚀…", shortened);
    }

    [Fact]
    public void ShorteningNothingIsNotAnError()
    {
        Assert.Equal(string.Empty, FeedText.Shorten(null, 10));
        Assert.Equal(string.Empty, FeedText.Shorten("", 10));
    }
}

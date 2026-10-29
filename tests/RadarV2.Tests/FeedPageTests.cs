using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// The feed envelope carries opportunities without losing anything it already carried.
///
/// Radar Intelligence is three things: developments in the subjects somebody follows, material
/// for their goals and challenges, and opportunities they can act on. The third lived only behind
/// its own screen, so the feed showed two thirds of what it is.
///
/// The risk in adding it is dull and real: the controller rebuilds this envelope field by field
/// from the projected page, so a field that is not copied is a field that silently disappears.
/// </summary>
public class FeedPageTests
{
    private static Opportunity Open(string title) => new()
    {
        Title = title,
        Organisation = "An organisation",
        Deadline = DateTime.UtcNow.AddDays(14),
    };

    /// <summary>
    /// A page of items is a general shape used elsewhere; opportunities are not part of what a
    /// page is, so this extends rather than widening it.
    /// </summary>
    [Fact]
    public void AFeedPageIsStillAPage()
    {
        Assert.IsAssignableFrom<PagedResult<object>>(new FeedPage());
    }

    /// <summary>
    /// Everything the envelope carried before is still on it, because it is rebuilt by hand.
    /// </summary>
    [Fact]
    public void TheEnvelopeKeepsWhatItAlreadyCarried()
    {
        var page = new FeedPage
        {
            Items = ["an item"],
            NextCursor = "a-cursor",
            HasMore = true,
            EmptyReason = FeedEmptyReason.NotEmpty,
            Opportunities = [Open("A grant")],
            OpportunityFocus = "Data analysis",
        };

        Assert.Single(page.Items);
        Assert.Equal("a-cursor", page.NextCursor);
        Assert.True(page.HasMore);
        Assert.Equal(FeedEmptyReason.NotEmpty, page.EmptyReason);
        Assert.Single(page.Opportunities);
        Assert.Equal("Data analysis", page.OpportunityFocus);
    }

    /// <summary>
    /// Nothing by default, so a later page or a filtered feed carries none rather than null.
    /// </summary>
    /// <remarks>
    /// They are a standing set rather than a stream: repeating them under every scroll would be
    /// the same handful of things over and over.
    /// </remarks>
    [Fact]
    public void APageCarriesNoOpportunitiesUnlessGivenSome()
    {
        var page = new FeedPage();

        Assert.Empty(page.Opportunities);
        Assert.Equal(string.Empty, page.OpportunityFocus);
    }

    /// <summary>
    /// The focus is reported so a reader can disagree with the match.
    /// </summary>
    /// <remarks>
    /// It used to default to "Cybersecurity", which told a brand new reader that Radar had
    /// matched against a focus they had never chosen.
    /// </remarks>
    [Fact]
    public void AnUnmatchedPageClaimsNoFocus()
    {
        Assert.Equal(string.Empty, new FeedPage { Opportunities = [Open("A grant")] }.OpportunityFocus);
    }
}

using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// A citation goes into somebody's bibliography, where being wrong is marked.
/// </summary>
/// <remarks>
/// Three things were wrong at once, and every one of them is visible to a marker. Author names
/// went in as the source gave them - "Ashish Vaswani" where a reference list wants
/// "Vaswani, A." The list collapsed to "et al." at three authors, which is the rule for an
/// in-text citation and not for a reference entry. And Harvard was a blend of Harvard and APA:
/// the year loose after a comma, the article title bare.
/// </remarks>
public class CitationStyleTests
{
    private static ContentItem Paper(params string[] authors) => new()
    {
        Id = "w1",
        Title = "Attention is all you need",
        Journal = "Advances in Neural Information Processing Systems",
        PublishedAt = new DateTime(2017, 12, 4, 0, 0, 0, DateTimeKind.Utc),
        Authors = [.. authors],
    };

    // ── author names ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ashish Vaswani", "Vaswani, A.")]
    [InlineData("Noam Shazeer", "Shazeer, N.")]
    [InlineData("Mary Jane Watson", "Watson, M. J.")]
    [InlineData("Jean-Luc Picard", "Picard, J.-L.")]
    [InlineData("Chimamanda", "Chimamanda")]
    public void ANameIsInvertedTheWayAReferenceListWantsIt(string given, string expected)
    {
        Assert.Equal(expected, AuthorName.Inverted(given));
    }

    /// <summary>A name the source already gave inverted is left alone.</summary>
    /// <remarks>
    /// Re-inverting "Vaswani, A." would make a surname out of an initial and produce
    /// "A., V." - a different person, confidently.
    /// </remarks>
    [Fact]
    public void ANameAlreadyInvertedIsNotInvertedAgain()
    {
        Assert.Equal("Vaswani, A.", AuthorName.Inverted("Vaswani, A."));
    }

    [Fact]
    public void ASuffixIsNotMistakenForTheSurname()
    {
        Assert.Equal("King, M. L., Jr.", AuthorName.Inverted("Martin Luther King Jr."));
    }

    // ── APA 7th ──────────────────────────────────────────────────────────────

    /// <summary>
    /// APA 7 lists every author in the reference entry, with an ampersand before the last.
    /// </summary>
    /// <remarks>
    /// "et al." at three authors is the in-text rule. In the reference list APA 7 names up to
    /// twenty - the sixth edition stopped at seven, which is the rule most sources still repeat.
    /// </remarks>
    [Fact]
    public void ApaNamesEveryAuthorRatherThanStoppingAtThree()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("Ashish Vaswani", "Noam Shazeer", "Niki Parmar"), "APA");

        Assert.Contains("Vaswani, A., Shazeer, N., & Parmar, N.", citation);
        Assert.DoesNotContain("et al.", citation);
    }

    [Fact]
    public void ApaPutsTheYearInBracketsAndTheDoiAsALink()
    {
        var paper = Paper("Ashish Vaswani");
        paper.Doi = "a-doi-on-record";

        var citation = OpenAlexClient.FormatCitation(paper, "APA");

        Assert.StartsWith("Vaswani, A. (2017). Attention is all you need.", citation);
        Assert.Contains("https://doi.org/a-doi-on-record", citation);
        // Sixth-edition forms, both dropped in the seventh.
        Assert.DoesNotContain("Retrieved from", citation);
        Assert.DoesNotContain("doi:", citation);
    }

    [Fact]
    public void ApaUsesAnEllipsisRatherThanEtAlPastTwenty()
    {
        var many = Enumerable.Range(1, 25).Select(i => $"Given{i} Surname{i}").ToArray();

        var citation = OpenAlexClient.FormatCitation(Paper(many), "APA");

        Assert.Contains("...", citation);
        Assert.Contains("Surname25, G.", citation);
        Assert.DoesNotContain("et al.", citation);
    }

    // ── Harvard ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Harvard as Cite Them Right sets it out: year in brackets, article title in single quotes.
    /// </summary>
    [Fact]
    public void HarvardPutsTheYearInBracketsAndQuotesTheTitle()
    {
        var citation = OpenAlexClient.FormatCitation(Paper("Ashish Vaswani"), "HARVARD");

        Assert.StartsWith("Vaswani, A. (2017) 'Attention is all you need',", citation);
    }

    /// <summary>Harvard joins with "and", not with the APA ampersand.</summary>
    [Fact]
    public void HarvardJoinsAuthorsWithAnd()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("Ashish Vaswani", "Noam Shazeer"), "HARVARD");

        Assert.Contains("Vaswani, A. and Shazeer, N.", citation);
        Assert.DoesNotContain("&", citation);
    }

    /// <summary>And does use et al. past three, which is its own rule.</summary>
    [Fact]
    public void HarvardUsesEtAlPastThreeAuthors()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("A One", "B Two", "C Three", "D Four"), "HARVARD");

        Assert.Contains("One, A. et al.", citation);
    }

    [Fact]
    public void HarvardGivesTheDoiWhereThereIsOne()
    {
        var paper = Paper("Ashish Vaswani");
        paper.Doi = "a-doi-on-record";

        Assert.Contains("doi: a-doi-on-record", OpenAlexClient.FormatCitation(paper, "HARVARD"));
    }

    // ── what is not known ────────────────────────────────────────────────────

    /// <summary>
    /// A paper with no recorded date is cited as n.d., which is what every style uses.
    /// </summary>
    /// <remarks>
    /// This printed the current year, so a work of unrecorded age was cited as published this
    /// year - a wrong date in a reference list, which is the one error a marker is certain to
    /// check.
    /// </remarks>
    [Theory]
    [InlineData("APA")]
    [InlineData("HARVARD")]
    public void APaperWithNoDateIsCitedAsNoDate(string style)
    {
        var paper = Paper("Ashish Vaswani");
        paper.PublishedAt = default;

        Assert.Contains("n.d.", OpenAlexClient.FormatCitation(paper, style));
    }

    /// <summary>A venue nothing recorded is left out rather than filled in.</summary>
    [Theory]
    [InlineData("APA")]
    [InlineData("HARVARD")]
    public void APaperWithNoVenueNamesNone(string style)
    {
        var paper = Paper("Ashish Vaswani");
        paper.Journal = null;
        paper.Source = string.Empty;
        paper.Doi = "a-doi-on-record";

        var citation = OpenAlexClient.FormatCitation(paper, style);

        Assert.DoesNotContain("Academic Press", citation);
        Assert.DoesNotContain("..", citation);
    }
}

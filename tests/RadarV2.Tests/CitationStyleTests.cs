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

    /// <summary>
    /// A surname particle belongs to the surname, not to the initials.
    /// </summary>
    /// <remarks>
    /// The last word was taken as the surname and everything before it initialised, so
    /// "Jan van der Berg" became "Berg, J. V. D." - not an abbreviation of his name but a
    /// different one. Dutch and Afrikaans surnames are ordinary in South African and Namibian
    /// scholarship and Arabic ones across North and West Africa, so this is a common name
    /// rendered as somebody else, in a reference list, confidently.
    /// </remarks>
    [Theory]
    [InlineData("Jan van der Berg", "Van der Berg, J.")]
    [InlineData("Pieter de Vries", "De Vries, P.")]
    [InlineData("Marie van Rooyen", "Van Rooyen, M.")]
    [InlineData("Juan Carlos de la Cruz", "De la Cruz, J. C.")]
    [InlineData("Ahmed bin Saeed", "Bin Saeed, A.")]
    [InlineData("Ngugi wa Thiong'o", "Wa Thiong'o, N.")]
    public void ASurnameParticleStaysWithTheSurname(string given, string expected)
    {
        Assert.Equal(expected, AuthorName.Inverted(given));
    }

    /// <summary>A title is not a name, and a degree is not part of a citation (APA 7 §9.8).</summary>
    /// <remarks>
    /// "Dr Ashish Vaswani" became "Vaswani, D. A." - an initial manufactured out of a courtesy
    /// title - and "Jane Smith MD" kept the degree, which APA omits entirely.
    /// </remarks>
    [Theory]
    [InlineData("Dr Ashish Vaswani", "Vaswani, A.")]
    [InlineData("Prof. Jane Smith", "Smith, J.")]
    [InlineData("Jane Smith MD", "Smith, J.")]
    [InlineData("Yao Ming PhD", "Ming, Y.")]
    public void ATitleOrADegreeIsNotPartOfTheName(string given, string expected)
    {
        Assert.Equal(expected, AuthorName.Inverted(given));
    }

    /// <summary>A surname with a suffix and no given name does not invert around the suffix.</summary>
    [Fact]
    public void ASuffixIsNotInvertedAroundWhenThereIsNoGivenName()
    {
        Assert.Equal("Vaswani, Jr.", AuthorName.Inverted("Vaswani Jr."));
    }

    /// <summary>
    /// A name written family-first in its own script is left as it is.
    /// </summary>
    /// <remarks>
    /// Chinese, Japanese and Korean names already lead with the family name, so inverting swaps
    /// the person round - and an "initial" taken from a logograph means nothing. APA 7 keeps
    /// them as given.
    /// </remarks>
    [Fact]
    public void ANameInItsOwnScriptIsLeftAlone()
    {
        Assert.Equal("李 明", AuthorName.Inverted("李 明"));
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

    // ── the other three styles ───────────────────────────────────────────────

    /// <summary>
    /// No style ends up with two full stops where one belongs.
    /// </summary>
    /// <remarks>
    /// A regression from making the author list style-aware. The author string now ends in an
    /// initial's period - "Vaswani, A." - and the MLA and Chicago templates hard-coded their own
    /// after it, so every entry in those two styles read "Vaswani, A.. \"Title.\"". The honesty
    /// test that guards against gaps only looked for ". " with a space, so it slipped through.
    /// </remarks>
    [Theory]
    [InlineData("APA")]
    [InlineData("HARVARD")]
    [InlineData("MLA")]
    [InlineData("CHICAGO")]
    [InlineData("IEEE")]
    public void NoStyleDoublesItsPunctuation(string style)
    {
        var citation = OpenAlexClient.FormatCitation(Paper("Ashish Vaswani", "Noam Shazeer"), style);

        Assert.DoesNotContain("..", citation);
        Assert.DoesNotContain(" ,", citation);
        Assert.DoesNotContain(", ,", citation);
    }

    /// <summary>MLA inverts the first author only, and writes given names in full.</summary>
    /// <remarks>
    /// MLA and Chicago fell through to the APA arm, so they carried APA initials and the APA
    /// ampersand into entries that want neither.
    /// </remarks>
    [Fact]
    public void MlaInvertsOnlyTheFirstAuthorAndWritesNamesInFull()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("Ashish Vaswani", "Noam Shazeer"), "MLA");

        Assert.Contains("Vaswani, Ashish, and Noam Shazeer", citation);
        Assert.DoesNotContain("&", citation);
    }

    /// <summary>And uses et al. from the third author, which is MLA's own rule.</summary>
    [Fact]
    public void MlaUsesEtAlFromTheThirdAuthor()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("Ashish Vaswani", "Noam Shazeer", "Niki Parmar"), "MLA");

        Assert.Contains("Vaswani, Ashish, et al.", citation);
    }

    /// <summary>Chicago has the same shape but names up to ten before et al.</summary>
    [Fact]
    public void ChicagoNamesThreeAuthorsRatherThanEtAl()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("Ashish Vaswani", "Noam Shazeer", "Niki Parmar"), "CHICAGO");

        Assert.Contains("Vaswani, Ashish, Noam Shazeer, and Niki Parmar", citation);
        Assert.DoesNotContain("et al.", citation);
    }

    /// <summary>IEEE puts the initials in front of the surname.</summary>
    /// <remarks>
    /// IEEE has its own formatter, so it was the one style the author work did not reach: it
    /// handed back the display name untouched, giving "Ashish Vaswani and Noam Shazeer".
    /// </remarks>
    [Fact]
    public void IeeePutsInitialsBeforeTheSurname()
    {
        var citation = OpenAlexClient.FormatCitation(
            Paper("Ashish Vaswani", "Noam Shazeer"), "IEEE");

        Assert.Contains("A. Vaswani and N. Shazeer", citation);
    }

    /// <summary>
    /// A style nobody wrote a template for is treated as APA, not as half of one.
    /// </summary>
    /// <remarks>
    /// The style comes straight off the query string, unvalidated. The fallback arm appended
    /// "https://doi.org/" unconditionally, so a paper with no DOI produced a citation ending in
    /// a link to nothing - inside a block the reader is invited to copy.
    /// </remarks>
    [Fact]
    public void AnUnknownStyleDoesNotProduceALinkToNothing()
    {
        var paper = Paper("Ashish Vaswani");
        paper.Url = string.Empty;

        var citation = OpenAlexClient.FormatCitation(paper, "Vancouver");

        Assert.DoesNotContain("https://doi.org/", citation);
        Assert.EndsWith("Advances in Neural Information Processing Systems.", citation);
    }

    // ── Harvard punctuation ──────────────────────────────────────────────────

    /// <summary>
    /// Harvard reads correctly whether or not the record names a venue.
    /// </summary>
    /// <remarks>
    /// The comma after the title was hard-coded while the venue can be absent, so a preprint -
    /// no journal, but a DOI, which is the ordinary shape of one - came out as
    /// "'Title', doi: 10.x", a comma running straight into the locator.
    /// </remarks>
    [Fact]
    public void HarvardReadsCorrectlyWithNoVenue()
    {
        var paper = Paper("Ashish Vaswani");
        paper.Journal = null;
        paper.Source = string.Empty;
        paper.Doi = "a-doi-on-record";

        Assert.Equal(
            "Vaswani, A. (2017) 'Attention is all you need'. doi: a-doi-on-record.",
            OpenAlexClient.FormatCitation(paper, "HARVARD"));
    }

    [Fact]
    public void HarvardEndsTheEntryWithAFullStop()
    {
        var paper = Paper("Ashish Vaswani");
        paper.Url = "https://example.org/paper";

        Assert.EndsWith(").", OpenAlexClient.FormatCitation(paper, "HARVARD"));
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

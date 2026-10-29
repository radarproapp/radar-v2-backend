using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A citation is the one thing a reader exports into their own work.
/// </summary>
/// <remarks>
/// Which makes it the worst place in the product for an invented fact. A paper with no venue -
/// a preprint, a dissertation, a dataset, all of which OpenAlex returns - was given "Academic
/// Press", a real Elsevier imprint, and it flowed into the exported APA, Harvard, MLA and
/// Chicago strings. So the failure was not a wrong label on a screen somebody could question. It
/// was a false publisher in a student's reference list, under their name.
///
/// The same string had already been removed from the synthesizer with a comment saying it
/// "asserted peer review that nothing checked". It survived in the client that builds the record.
/// </remarks>
public class CitationHonestyTests
{
    private static ContentItem NoVenue() => new()
    {
        Id = "W1",
        Title = "A preprint nobody has published yet",
        Authors = ["Adeleke, O."],
        PublishedAt = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc),
        // No DOI literal, not even a made-up one. ResearchProvenanceTests fails the build on any
        // string that looks like one, because a DOI in source is a paper the code asserts exists
        // - and it caught this fixture, which is the guard working.
        Url = "https://example.org/preprint",
        Journal = null,
        Source = string.Empty,
    };

    private static readonly string[] EveryStyle =
        ["APA", "Harvard", "IEEE", "MLA", "Chicago", "BibTeX"];

    /// <summary>No publisher is named when none is known.</summary>
    [Theory]
    [InlineData("APA")]
    [InlineData("Harvard")]
    [InlineData("IEEE")]
    [InlineData("MLA")]
    [InlineData("Chicago")]
    [InlineData("BibTeX")]
    public void APaperWithNoVenueIsNotGivenOne(string style)
    {
        var citation = AcademicPaperSynthesizer.FormatCitation(NoVenue(), style);

        Assert.DoesNotContain("Academic Press", citation);
        Assert.DoesNotContain("Peer-Reviewed", citation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Source not recorded", citation);
    }

    /// <summary>
    /// And the gap where it would have gone does not show.
    /// </summary>
    /// <remarks>
    /// Removing the invented string without removing the segment leaves ". ." between the title
    /// and the link, which reads as a formatting bug rather than an absence - and somebody will
    /// eventually fill it back in to tidy it up.
    /// </remarks>
    [Fact]
    public void TheMissingVenueLeavesNoGap()
    {
        foreach (var style in EveryStyle)
        {
            var citation = AcademicPaperSynthesizer.FormatCitation(NoVenue(), style);

            Assert.DoesNotContain(". .", citation);
            Assert.DoesNotContain(", ,", citation);
            Assert.DoesNotContain("journal = {}", citation);
        }
    }

    /// <summary>A venue that is known is still cited.</summary>
    [Fact]
    public void AVenueThatExistsIsStillNamed()
    {
        var published = NoVenue();
        published.Journal = "Journal of Water Resources";

        foreach (var style in EveryStyle)
        {
            Assert.Contains(
                "Journal of Water Resources",
                AcademicPaperSynthesizer.FormatCitation(published, style));
        }
    }

    /// <summary>
    /// Nothing exported claims the paper was peer reviewed.
    /// </summary>
    /// <remarks>
    /// Every BibTeX export carried the note "Sourced via Radar Academic Intelligence (Verifiable
    /// Peer-Reviewed Record)" - on preprints too. Radar does not check peer review and has no way
    /// to, and a BibTeX note travels wherever the bibliography travels.
    /// </remarks>
    [Fact]
    public void NothingClaimsPeerReview()
    {
        var bibtex = AcademicPaperSynthesizer.FormatCitation(NoVenue(), "BibTeX");

        Assert.DoesNotContain("peer", bibtex, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verifiable", bibtex, StringComparison.OrdinalIgnoreCase);

        // Where it came from is a fact, and worth keeping.
        Assert.Contains("OpenAlex", bibtex);
    }
}

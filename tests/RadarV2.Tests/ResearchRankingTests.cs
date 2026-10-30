using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Which research is put first, under a spec that asks for credible and relevant work.
/// </summary>
/// <remarks>
/// Nothing ranked these. Search handed back whatever order OpenAlex sent, and the library
/// sorted on the publication date alone - so it was "the newest research Radar holds", and a
/// foundational paper could not appear at all, because something is always newer.
/// </remarks>
public class ResearchRankingTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    private static ContentItem Paper(
        string title,
        int year,
        int citations = 0,
        string? journal = "Nature",
        string? doi = "a-doi-is-on-record",
        params string[] authors) => new()
    {
        Id = title,
        Title = title,
        Type = ContentType.ResearchPaper,
        PublishedAt = new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        CitationCount = citations,
        Journal = journal,
        Doi = doi,
        Authors = [.. authors.Length > 0 ? authors : ["A. Researcher"]],
    };

    private static UserProfile Reader() => new()
    {
        Persona = PersonaType.Student,
        PersonaChosenAt = Now,
        Interests = ["Malaria"],
        Goals = ["Conduct research"],
        PrimaryGoal = "Conduct research",
    };

    // ── era ──────────────────────────────────────────────────────────────────

    [Fact]
    public void WorkFromTheLastThreeYearsIsRecent()
    {
        Assert.Equal(ResearchRanking.Recent, ResearchRanking.EraOf(Paper("New", 2025), Now));
    }

    /// <summary>Old and uncited is not the same as foundational.</summary>
    /// <remarks>
    /// The era used to be read off the year alone, so anything past fifteen years was called
    /// "Foundational - predates current practice by design" whether or not a soul had cited it.
    /// Most of what gets published is never cited again. Telling a student that an ignored 2004
    /// paper is the text their field rests on is a claim, and a false one.
    /// </remarks>
    [Fact]
    public void OldAndUncitedIsEstablishedRatherThanFoundational()
    {
        Assert.Equal(ResearchRanking.Established, ResearchRanking.EraOf(Paper("Ignored", 1998, citations: 3), Now));
    }

    [Fact]
    public void OldAndStillCitedIsFoundational()
    {
        Assert.Equal(ResearchRanking.Foundational, ResearchRanking.EraOf(Paper("Landmark", 1998, citations: 20_000), Now));
    }

    // ── ordering ─────────────────────────────────────────────────────────────

    /// <summary>Relevance to the reader outranks standing in the field.</summary>
    /// <remarks>
    /// The order the spec names: credible and relevant research, where relevance is to this
    /// person. A paper everybody cites, about something nobody asked for, is not the answer to
    /// a search - and ranking on citations alone would have put it first every time.
    /// </remarks>
    [Fact]
    public void APaperOnWhatTheReaderAskedForBeatsAMoreCitedOneThatIsNotAbout()
    {
        var theirs = Paper("Malaria transmission in the Sahel", 2024, citations: 12);
        var famous = Paper("A landmark result in particle physics", 2024, citations: 50_000);

        var ordered = ResearchRanking.Ordered([famous, theirs], Reader(), Now);

        Assert.Equal(theirs.Title, ordered[0].Title);
    }

    /// <summary>Between two equally relevant papers, standing decides.</summary>
    [Fact]
    public void BetweenTwoRelevantPapersTheBetterCitedComesFirst()
    {
        var cited = Paper("Malaria vaccine trial results", 2024, citations: 4_000);
        var not = Paper("Malaria vaccine trial notes", 2024, citations: 0);

        var ordered = ResearchRanking.Ordered([not, cited], Reader(), Now);

        Assert.Equal(cited.Title, ordered[0].Title);
    }

    /// <summary>
    /// A paper nobody can open or attribute ranks below one they can.
    /// </summary>
    /// <remarks>
    /// The spec asks for authors, a publication source and a link to the original on every
    /// result. A record with none of them is not a result, it is a rumour - and the answer is to
    /// rank it below, never to fill the gaps in.
    /// </remarks>
    [Fact]
    public void ATraceablePaperBeatsATitleAndNothingElse()
    {
        var traceable = Paper("Malaria control in Kano", 2024, citations: 5);
        var bare = new ContentItem
        {
            Id = "bare",
            Title = "Malaria control in Kano",
            Type = ContentType.ResearchPaper,
            PublishedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var ordered = ResearchRanking.Ordered([bare, traceable], Reader(), Now);

        Assert.Equal("Malaria control in Kano", ordered[0].Title);
        Assert.NotEmpty(ordered[0].Authors);
    }

    /// <summary>Both ends of the spec's ask surface over the middle.</summary>
    /// <remarks>
    /// "Recent publications and foundational papers where appropriate" is two groups, not one.
    /// Sorting by date put the first at the top and made the second unreachable; sorting by
    /// citations alone would do the reverse.
    /// </remarks>
    [Fact]
    public void RecentWorkAndFoundationalWorkBothOutrankTheMiddle()
    {
        var recent = Paper("Malaria sequencing, 2025", 2025, citations: 10);
        var middling = Paper("Malaria notes, 2017", 2017, citations: 10);
        var foundational = Paper("Malaria, the original study", 1996, citations: 30_000);

        var ordered = ResearchRanking.Ordered([middling, recent, foundational], Reader(), Now);

        Assert.Equal("Malaria notes, 2017", ordered[2].Title);
    }

    /// <summary>Signed out, there is no reader to be relevant to, and it still orders.</summary>
    [Fact]
    public void WithoutAReaderItStillPutsTheCredibleWorkFirst()
    {
        var solid = Paper("Something well cited", 2024, citations: 900);
        var thin = Paper("Something uncited", 2024, citations: 0, journal: null, doi: null);

        var ordered = ResearchRanking.Ordered([thin, solid], reader: null, now: Now);

        Assert.Equal(solid.Title, ordered[0].Title);
    }

    /// <summary>Citations cannot run away with the ranking.</summary>
    /// <remarks>
    /// Bounded and logarithmic. Linear, one landmark paper would head every search regardless of
    /// what was searched for, which is the failure mode of ranking on citations at all.
    /// </remarks>
    [Fact]
    public void StandingIsBoundedSoOneLandmarkCannotDominate()
    {
        Assert.True(
            ResearchRanking.StandingOf(Paper("x", 2024, citations: 1_000_000))
            <= ResearchRanking.MostStandingIsWorth);

        // Below what a stated interest is worth, which is the whole point of the cap.
        Assert.True(ResearchRanking.MostStandingIsWorth < 10);
        Assert.Equal(0, ResearchRanking.StandingOf(Paper("y", 2024, citations: 0)));
    }
}

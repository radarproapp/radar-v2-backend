using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Which research to put first, and how old is too old to call current.
/// </summary>
/// <remarks>
/// Research Discovery returned results in whatever order OpenAlex sent them. Nothing sorted
/// them - not by how well they matched the reader, not by how well cited they were - under a
/// spec that says to "prioritise credible and relevant research, including recent publications
/// and foundational papers where appropriate".
///
/// Three things decide it, in the order the spec names them. Relevance to this reader comes
/// first and is the existing deterministic scorer, so a paper's standing in its field cannot
/// outrank its having nothing to do with what somebody asked for. Credibility is citations,
/// a named venue, and a link somebody can actually open. Age is last, and is not a penalty -
/// see <see cref="EraOf"/>.
///
/// Nothing here invents anything. Every input is a field OpenAlex reported.
/// </remarks>
public static class ResearchRanking
{
    /// <summary>Where a paper sits in time, which is not the same as how good it is.</summary>
    public const string Recent = "Recent";
    public const string Established = "Established";
    public const string Foundational = "Foundational";

    /// <summary>Published within this many years counts as current work.</summary>
    private const int RecentYears = 3;

    /// <summary>Past this, a paper is either foundational or simply old.</summary>
    private const int EstablishedYears = 12;

    /// <summary>
    /// What it takes for older work to still be the thing to read.
    /// </summary>
    /// <remarks>
    /// Citations are the only evidence available that a twenty-year-old paper is a foundation
    /// rather than a dead end, and the threshold is deliberately high: the point of saying
    /// "Foundational" is that it means something. Below it, old work is just old, and is ranked
    /// as such without being hidden - the reader can still find it and decide.
    /// </remarks>
    private const int CitationsThatOutliveAPaper = 500;

    /// <summary>
    /// Where this paper sits in time: recent, established, or foundational.
    /// </summary>
    /// <remarks>
    /// Said out loud because the alternative is a reader noticing a year and drawing the
    /// conclusion themselves, which is what nobody does while skimming. A 1998 paper can be the
    /// text its whole field rests on and still be the wrong thing to copy as current practice;
    /// a 2024 preprint can be neither. Both facts are useful and neither is a score.
    /// </remarks>
    public static string? EraOf(ContentItem paper, DateTime? now = null)
    {
        // No date, no era. The guard this replaces asked whether the year was zero or less,
        // which no DateTime can be - default(DateTime) is year 1 - so it never fired, and a
        // record with no publication date was quietly aged at two thousand years and labelled
        // "Established". An age bracket asserted about a date Radar does not have.
        if (paper.PublishedAt == default) return null;

        var age = ((now ?? DateTime.UtcNow) - paper.PublishedAt).TotalDays / 365.25;

        if (age <= RecentYears) return Recent;
        if (age <= EstablishedYears) return Established;

        return paper.CitationCount >= CitationsThatOutliveAPaper ? Foundational : Established;
    }

    /// <summary>
    /// How much a paper's standing in its field is worth, bounded.
    /// </summary>
    /// <remarks>
    /// Logarithmic, because the difference between 10 citations and 100 is real and the
    /// difference between 4,000 and 40,000 is not a difference in whether somebody should read
    /// it. Linear, a single landmark paper would sit at the top of every search regardless of
    /// what was searched for.
    ///
    /// And capped below what a stated interest is worth. The spec puts relevance to the reader
    /// ahead of credibility, and the first cap here was 16 against an interest's 10 - so a
    /// much-cited paper on particle physics outranked a lightly-cited one on the malaria the
    /// reader actually searched for. Citations say a paper matters to its field. They say
    /// nothing about whether it matters to the person asking.
    /// </remarks>
    public const double MostStandingIsWorth = 8;

    public static double StandingOf(ContentItem paper)
    {
        if (paper.CitationCount <= 0) return 0;

        return Math.Min(Math.Log10(paper.CitationCount) * 2, MostStandingIsWorth);
    }

    /// <summary>
    /// Whether the reader can actually get at this, and tell who wrote it.
    /// </summary>
    /// <remarks>
    /// A paper behind a title and nothing else is not a result, it is a rumour. The spec asks
    /// for authors, a publication source and a link to the original for every item, so a record
    /// carrying all three is worth more than one that is missing them - and a record missing
    /// them is never invented to fill the gap.
    /// </remarks>
    public static double TraceabilityOf(ContentItem paper)
    {
        var points = 0.0;

        if (paper.Authors.Count > 0) points += 3;
        if (!string.IsNullOrWhiteSpace(paper.Journal)) points += 3;
        if (!string.IsNullOrWhiteSpace(paper.Doi)) points += 3;

        // Something a reader can open today, rather than a paywall and an apology.
        if (!string.IsNullOrWhiteSpace(paper.OpenAccessPdfUrl)) points += 3;

        return points;
    }

    /// <summary>
    /// The whole judgement, for one paper against one reader.
    /// </summary>
    /// <remarks>
    /// The reader may be null - search works signed out, and several callers have no profile in
    /// scope - in which case relevance contributes nothing and the ordering falls back to
    /// standing and traceability. That is the right answer for an anonymous search: without
    /// knowing who is asking, the best Radar can do is put the credible, reachable work first.
    /// </remarks>
    public static double Score(ContentItem paper, UserProfile? reader, DateTime? now = null)
    {
        var relevance = reader is not null
            ? InterestPersonalization.ContentScore(paper, reader)
            : 0;

        // Era is a label, not a ranking. The one place it moves anything is here, and only
        // upward: current work and the work a field is built on are both what the spec asks to
        // surface, and the long tail in between is what it does not.
        var era = EraOf(paper, now) switch
        {
            Recent => 6.0,
            Foundational => 6.0,
            _ => 0.0,
        };

        return relevance + StandingOf(paper) + TraceabilityOf(paper) + era;
    }

    /// <summary>
    /// The papers, best first. Stable within equal scores, so a repeated search does not
    /// reshuffle results the reader was halfway through reading.
    /// </summary>
    public static List<ContentItem> Ordered(IEnumerable<ContentItem> papers, UserProfile? reader, DateTime? now = null) =>
        [.. papers.OrderByDescending(paper => Score(paper, reader, now))];
}

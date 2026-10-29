using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Turning a set of criterion verdicts into a mark out of ten.
/// </summary>
/// <remarks>
/// The score is arithmetic, not an opinion. A model is asked whether each criterion is met, partly
/// met or not met — a judgement it can evidence and a reader can argue with — and the number
/// falls out of those. It is never asked for a score.
///
/// That matters for three reasons. A number asked for directly drifts between two runs on the
/// same work, because nothing constrains it. It cannot be checked, because there is no account of
/// how it was reached. And section 16 forbids an unexplained score outright — which a holistic
/// "7 out of 10" always is, however confident it sounds.
///
/// This way the mark is the criteria: a learner who disagrees with a 5 can see exactly which
/// three things were not met, and what would move each of them.
/// </remarks>
public static class ProjectScore
{
    /// <summary>
    /// The mark a project has to reach to be accepted.
    /// </summary>
    /// <remarks>
    /// Six out of ten. Below it the work is returned with what to fix rather than failed: the
    /// point of a bar is to be cleared on the second attempt, which is why submissions are kept
    /// as a list.
    /// </remarks>
    public const int PassMark = 6;

    /// <summary>
    /// What each verdict is worth.
    /// </summary>
    /// <remarks>
    /// Partly is worth half rather than nothing. Work that does the thing but does not evidence
    /// it is not the same as work that does not do the thing, and a scale that treats them alike
    /// tells a learner nothing about which of the two they have.
    /// </remarks>
    private static double Weight(string verdict) => verdict switch
    {
        "Met" => 1.0,
        "Partly" => 0.5,
        _ => 0.0,
    };

    /// <summary>
    /// The mark out of ten, from one to ten.
    /// </summary>
    /// <remarks>
    /// One at the bottom rather than zero, because the scale the learner was told about is one to
    /// ten and a zero would be off it. Nothing assessed at all is not a score and returns null —
    /// an empty rubric scoring 1 would read as "this is terrible" when it means "nothing was
    /// judged".
    /// </remarks>
    public static int? Of(IReadOnlyCollection<AssessmentCriterion> criteria)
    {
        if (criteria.Count == 0) return null;

        // Unjudged criteria are counted in the denominator but score nothing. A model that
        // skipped three of eight has not thereby made the work better, and dropping them from
        // the total would round the gaps away.
        var earned = criteria.Sum(c => Weight(c.Verdict));
        var mark = (int)Math.Round(1 + 9 * (earned / criteria.Count), MidpointRounding.AwayFromZero);

        // A criterion that is not met at all keeps the work below the bar, whatever the rest
        // scored.
        //
        // Measured on production, twice. A two-line document reading "2 + 2 = 4" first scored
        // nine, because the bar for a verdict had never been defined. With that fixed the same
        // document came back honestly judged - two Partly, one NotMet, two Met - and still scored
        // six, because three points out of five is sixty per cent and sixty per cent reached the
        // pass mark. So the prompt was right and the arithmetic was wrong, and the arithmetic was
        // mine: I chose the mapping and never worked out what it allowed.
        //
        // Accepted has to mean the standard was met. A standard with one of its criteria entirely
        // unmet has not been, and the fix is specific - which is more use to a learner than a
        // pass they cannot learn from.
        //
        // The mark still shows how far off it is: a project with one gap scores five, not one.
        var unmet = criteria.Any(c => c.Verdict is "NotMet");

        return unmet ? Math.Min(mark, PassMark - 1) : mark;
    }

    /// <summary>Whether this mark clears the bar.</summary>
    public static bool IsAccepted(int? score) => score is >= PassMark;

    /// <summary>
    /// What to say about a mark, in the words a learner reads.
    /// </summary>
    /// <remarks>
    /// Never "good" or "poor". The verdict is what the criteria say, and the sentence points at
    /// the next action rather than passing comment on the person.
    /// </remarks>
    public static string Verdict(int? score) => score switch
    {
        null => "Nothing was assessed, so there is no mark.",
        >= 9 => "Accepted. This meets the standard for this stage with very little left open.",
        >= PassMark => "Accepted. It clears the bar, and the criteria below show what is still open.",
        >= 4 => "Not accepted yet. The criteria below say what is missing; fixing them is a revision, not a restart.",
        _ => "Not accepted. The criteria below are mostly unmet, so this needs another pass before it is worth reviewing again.",
    };
}

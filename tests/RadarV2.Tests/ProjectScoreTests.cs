using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A project is marked out of ten, and accepted at six.
///
/// The mark is arithmetic rather than an opinion. A model says whether each criterion is met,
/// partly met or not met — a judgement it can evidence and a learner can argue with — and the
/// number falls out of those. Asking for a score directly produces one that drifts between runs
/// on the same work and that nobody can check, which section 16 forbids outright.
/// </summary>
public class ProjectScoreTests
{
    private static AssessmentCriterion C(string verdict) =>
        new() { Name = "A criterion", Asks = "Something", Verdict = verdict };

    private static List<AssessmentCriterion> All(string verdict, int count = 8) =>
        [.. Enumerable.Range(0, count).Select(_ => C(verdict))];

    private static List<AssessmentCriterion> SomeOf(string first, int firstCount, string second, int secondCount) =>
        [.. All(first, firstCount), .. All(second, secondCount)];

    [Fact]
    public void EverythingMetIsTenOutOfTen()
    {
        Assert.Equal(10, ProjectScore.Of(All("Met")));
    }

    /// <summary>
    /// Nothing met is one, not zero. The scale a learner was told about starts at one, and a zero
    /// would be off it.
    /// </summary>
    [Fact]
    public void NothingMetIsOneNotZero()
    {
        Assert.Equal(1, ProjectScore.Of(All("NotMet")));
    }

    /// <summary>
    /// Partly is worth half. Work that does the thing but does not evidence it is not the same as
    /// work that does not do the thing.
    /// </summary>
    [Fact]
    public void PartlyCountsForHalf()
    {
        var partly = ProjectScore.Of(All("Partly"));

        Assert.True(partly > ProjectScore.Of(All("NotMet")), $"Everything partly met scored {partly}.");
        Assert.True(partly < ProjectScore.Of(All("Met")), $"Everything partly met scored {partly}.");
    }

    /// <summary>
    /// Nothing assessed is not a mark of one. An empty rubric scoring the bottom of the scale
    /// would read as "this is terrible" when it means "nothing was judged".
    /// </summary>
    [Fact]
    public void NothingAssessedIsNotAMark()
    {
        Assert.Null(ProjectScore.Of([]));
        Assert.False(ProjectScore.IsAccepted(null));
        Assert.Contains("no mark", ProjectScore.Verdict(null));
    }

    /// <summary>
    /// A criterion the model skipped counts against the total.
    /// </summary>
    /// <remarks>
    /// It is in the denominator and earns nothing. Dropping unjudged criteria would round the
    /// gaps away: a model that answered three of eight would score as if the other five did not
    /// exist, and skipping is not the same as passing.
    /// </remarks>
    [Fact]
    public void ASkippedCriterionDoesNotImproveTheMark()
    {
        var judged = new List<AssessmentCriterion> { C("Met"), C("Met"), C("Met"), C("Met") };
        var withSkips = new List<AssessmentCriterion>(judged) { C("Unjudged"), C("Unjudged") };

        Assert.True(
            ProjectScore.Of(withSkips) < ProjectScore.Of(judged),
            "Leaving criteria unjudged scored the same as meeting them.");
    }

    // ── The bar ─────────────────────────────────────────────────────────────

    [Fact]
    public void SixIsAccepted()
    {
        Assert.True(ProjectScore.IsAccepted(6));
        Assert.True(ProjectScore.IsAccepted(10));
    }

    [Fact]
    public void FiveIsNot()
    {
        Assert.False(ProjectScore.IsAccepted(5));
        Assert.False(ProjectScore.IsAccepted(1));
    }

    /// <summary>
    /// Below the bar is a revision, not a failure.
    /// </summary>
    /// <remarks>
    /// The point of a bar is to be cleared on the second attempt, which is why submissions are
    /// kept as a list rather than overwritten.
    /// </remarks>
    [Fact]
    public void BelowTheBarPointsAtTheNextAttempt()
    {
        var verdict = ProjectScore.Verdict(4);

        Assert.Contains("Not accepted", verdict);
        Assert.Contains("revision", verdict);
    }

    /// <summary>
    /// And the wording never passes comment on the person.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(10)]
    public void TheVerdictJudgesTheWorkAndNotTheLearner(int score)
    {
        var verdict = ProjectScore.Verdict(score).ToLowerInvariant();

        foreach (var word in new[] { "poor", "bad", "weak", "excellent", "impressive", "well done" })
        {
            Assert.DoesNotContain(word, verdict);
        }
    }

    // ── The bar, as measured on production ──────────────────────────────────

    /// <summary>
    /// A criterion that is not met at all keeps the work below the bar.
    /// </summary>
    /// <remarks>
    /// The exact shape that reached production. A two-line document reading "2 + 2 = 4" was
    /// judged honestly - two Partly, one NotMet, two Met - and still scored six, because three
    /// points out of five is sixty per cent and sixty per cent reached the pass mark. The prompt
    /// was right; the arithmetic was wrong.
    /// </remarks>
    [Fact]
    public void TheShapeThatPassedOnProductionNoLongerPasses()
    {
        var thin = new List<AssessmentCriterion>
        {
            C("Partly"), C("Partly"), C("NotMet"), C("Met"), C("Met"),
        };

        var score = ProjectScore.Of(thin);

        Assert.False(ProjectScore.IsAccepted(score), $"Scored {score}, which still clears the bar.");
    }

    /// <summary>
    /// Strong work with one gap is held back too, and that is the point.
    /// </summary>
    /// <remarks>
    /// Accepted has to mean the standard was met, and a standard with one of its criteria
    /// entirely unmet has not been. The fix is specific, which is more use than a pass nobody can
    /// learn from.
    /// </remarks>
    [Fact]
    public void OneUnmetCriterionHoldsBackOtherwiseStrongWork()
    {
        var nearlyThere = new List<AssessmentCriterion>
        {
            C("Met"), C("Met"), C("Met"), C("Met"), C("Met"), C("Met"), C("NotMet"),
        };

        Assert.False(ProjectScore.IsAccepted(ProjectScore.Of(nearlyThere)));
    }

    /// <summary>
    /// The mark still says how far off it is. One gap is a five, not a one.
    /// </summary>
    [Fact]
    public void TheMarkStillShowsHowCloseTheWorkIs()
    {
        var oneGap = new List<AssessmentCriterion>
        {
            C("Met"), C("Met"), C("Met"), C("Met"), C("Met"), C("Met"), C("NotMet"),
        };

        var mostlyMissing = All("NotMet");

        Assert.Equal(ProjectScore.PassMark - 1, ProjectScore.Of(oneGap));
        Assert.True(ProjectScore.Of(oneGap) > ProjectScore.Of(mostlyMissing));
    }

    /// <summary>Work with nothing unmet is still marked on what it earned.</summary>
    [Fact]
    public void NothingUnmetIsStillMarkedNormally()
    {
        Assert.Equal(10, ProjectScore.Of(All("Met")));
        Assert.Equal(ProjectScore.PassMark + 2, ProjectScore.Of(SomeOf("Met", 6, "Partly", 2)));
    }

    /// <summary>
    /// Work where every criterion is only partly met does not clear the bar.
    /// </summary>
    /// <remarks>
    /// Half the points available is five and a half on a scale from one to ten, and rounding to
    /// the nearest whole number made that a six. So eight criteria of which not one was fully met
    /// was accepted work, which is the opposite of what a bar is for. The mark is rounded down
    /// now: never nudged across the bar by arithmetic.
    ///
    /// This is a different gap from the NotMet rule below it. There the model said outright that
    /// something was missing; here it said every single thing was half done, and no cap would
    /// have caught it.
    /// </remarks>
    [Fact]
    public void EverythingOnlyPartlyMetDoesNotPass()
    {
        var score = ProjectScore.Of(All("Partly"));

        Assert.False(ProjectScore.IsAccepted(score), $"Nothing was fully met and it scored {score}.");
    }

    /// <summary>
    /// And a mark is never rounded up, at any point on the scale.
    /// </summary>
    [Fact]
    public void AMarkIsNeverRoundedUp()
    {
        // Three of four met is three quarters of the way up, which is 7.75.
        Assert.Equal(7, ProjectScore.Of(SomeOf("Met", 3, "Unjudged", 1)));
    }
}

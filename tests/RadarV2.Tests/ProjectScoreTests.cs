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
        // Two met and one partly is five sixths of the way up, which is 8.5.
        Assert.Equal(8, ProjectScore.Of(SomeOf("Met", 2, "Partly", 1)));
    }

    /// <summary>
    /// A criterion nobody judged cannot carry the work over the bar either.
    /// </summary>
    /// <remarks>
    /// Measured: six met and one unmet scored five and was refused, while six met and one never
    /// looked at scored eight and was accepted. So an unanswered criterion was worth more than a
    /// failed one, and "we did not check" outranked "we checked and it failed".
    ///
    /// The service refuses to record a mark at all for a half-answered rubric, which is the
    /// better place to catch this. The cap stays because the invariant belongs with the
    /// arithmetic: nothing out of here clears the bar unless every criterion was judged.
    /// </remarks>
    [Fact]
    public void AnUnjudgedCriterionCannotCarryTheWorkOverTheBar()
    {
        var mostlyAnswered = SomeOf("Met", 6, "Unjudged", 1);
        var oneFailed = SomeOf("Met", 6, "NotMet", 1);

        Assert.False(
            ProjectScore.IsAccepted(ProjectScore.Of(mostlyAnswered)),
            $"Six met and one unjudged scored {ProjectScore.Of(mostlyAnswered)}.");

        // And no better than the same work with that criterion judged and failed.
        Assert.Equal(ProjectScore.Of(oneFailed), ProjectScore.Of(mostlyAnswered));
    }

    /// <summary>
    /// Checked at the rubric sizes that actually ship, not only at a convenient eight.
    /// </summary>
    /// <remarks>
    /// ProjectStandards hands out five criteria at Beginner, six at Intermediate and seven at
    /// Advanced, counting the evidence criterion for the kind of submission. A scale that behaves
    /// at eight and not at six is a scale nobody is using.
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void TheBarHoldsAtEveryRubricSizeThatShips(int size)
    {
        Assert.Equal(10, ProjectScore.Of(All("Met", size)));
        Assert.Equal(1, ProjectScore.Of(All("NotMet", size)));

        // Nothing fully met does not pass, whatever the rubric length.
        Assert.False(ProjectScore.IsAccepted(ProjectScore.Of(All("Partly", size))));

        // One gap holds the work back, and the mark still says it is close.
        var oneGap = SomeOf("Met", size - 1, "NotMet", 1);
        Assert.Equal(ProjectScore.PassMark - 1, ProjectScore.Of(oneGap));
    }

    /// <summary>The mark stays on the scale a learner was told about, whatever the shape.</summary>
    [Theory]
    [InlineData("Met")]
    [InlineData("Partly")]
    [InlineData("NotMet")]
    [InlineData("Unjudged")]
    public void TheMarkIsAlwaysBetweenOneAndTen(string verdict)
    {
        for (var size = 1; size <= 9; size++)
        {
            var mark = ProjectScore.Of(All(verdict, size));

            Assert.NotNull(mark);
            Assert.InRange(mark!.Value, 1, 10);
        }
    }

    // ── Acceptance after a revision ─────────────────────────────────────────

    /// <summary>
    /// An assessment as it is stored: a mark, a flag, and the criteria it came from.
    /// </summary>
    private static ProjectAssessment Scored(int score, List<AssessmentCriterion>? criteria = null) => new()
    {
        Score = score,
        IsAccepted = ProjectScore.IsAccepted(score),
        Criteria = criteria ?? All(score >= 10 ? "Met" : score >= ProjectScore.PassMark ? "Met" : "NotMet", 5),
    };

    /// <summary>
    /// A project revised into something worse is not an accepted project any more.
    /// </summary>
    /// <remarks>
    /// The flag was permanent once set. Pass once, then replace the work with anything at all,
    /// and the project stayed accepted - on the learner's own list and on the public showcase
    /// page, which reads that flag and nothing else. So the bar could be cleared once and then
    /// edited around.
    /// </remarks>
    [Fact]
    public void AcceptanceIsWithdrawnWhenARevisionFallsBelowTheBar()
    {
        Assert.False(ProjectScore.SurvivesRevision(wasAccepted: true, Scored(4)));
    }

    /// <summary>A revision that still clears the bar keeps it.</summary>
    [Fact]
    public void AcceptanceSurvivesARevisionThatStillClearsTheBar()
    {
        Assert.True(ProjectScore.SurvivesRevision(wasAccepted: true, Scored(ProjectScore.PassMark)));
        Assert.True(ProjectScore.SurvivesRevision(wasAccepted: true, Scored(10)));
    }

    /// <summary>
    /// And handing in work that clears the bar does not accept the project by itself.
    /// </summary>
    /// <remarks>
    /// Clearing the bar makes a project eligible. Marking it finished is still the learner's
    /// call, which is the whole difference between "this is good enough" and "I am done with it".
    /// </remarks>
    [Fact]
    public void ClearingTheBarDoesNotAcceptAProjectOnItsOwn()
    {
        Assert.False(ProjectScore.SurvivesRevision(wasAccepted: false, Scored(10)));
    }

    /// <summary>
    /// An attempt that could not be assessed does not hold acceptance open either.
    /// </summary>
    /// <remarks>
    /// A model timing out is not evidence that the new work meets the standard. Withdrawing is
    /// recoverable - submit again and it is re-judged - where leaving it set is not.
    /// </remarks>
    [Fact]
    public void AnUnassessedRevisionDoesNotKeepAcceptance()
    {
        Assert.False(ProjectScore.SurvivesRevision(wasAccepted: true, null));
        Assert.False(ProjectScore.SurvivesRevision(
            wasAccepted: true,
            new ProjectAssessment { Score = null, IsAccepted = false }));
    }

    /// <summary>
    /// A correction to the bar applies to work already submitted.
    /// </summary>
    /// <remarks>
    /// Acceptance was read from the stored flag. An assessment recorded before the arithmetic was
    /// fixed carries IsAccepted true on all-partly work, so trusting the flag would have made the
    /// fix a cut-off date rather than a correction: the same shape refused today stayed accepted
    /// from yesterday. It is worked out from the criteria instead, which are the record of what
    /// was actually judged.
    /// </remarks>
    [Fact]
    public void AnOldAcceptanceOnWorkThatNoLongerPassesDoesNotHold()
    {
        var recordedUnderTheOldBar = new ProjectAssessment
        {
            Score = ProjectScore.PassMark,
            IsAccepted = true,
            Criteria = All("Partly", 5),
        };

        Assert.False(ProjectScore.SurvivesRevision(wasAccepted: true, recordedUnderTheOldBar));
    }

    /// <summary>And a submission that was never assessed is not evidence either way.</summary>
    [Fact]
    public void AnAssessmentWithNoMarkIsNotAnAcceptance()
    {
        Assert.False(ProjectScore.SurvivesRevision(
            wasAccepted: true,
            new ProjectAssessment { Score = null, IsAccepted = true, Criteria = All("Met", 5) }));
    }
}

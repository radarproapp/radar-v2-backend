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
        var score = ProjectScore.Of(All("Partly"));

        Assert.True(score is > 5 and < 7, $"Everything partly met scored {score}.");
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
}

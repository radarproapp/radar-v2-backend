using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// What a project is judged against is written down, not invented per run.
///
/// Authored for the same reason the document rubrics are: a standard is a claim about what good
/// work looks like, and it belongs in the product where somebody can disagree with it. A model
/// asked to invent the criteria and apply them leaves nobody knowing what the judgement was
/// against.
/// </summary>
public class ProjectStandardsTests
{
    /// <summary>
    /// A first project is judged on finishing, not on ambition.
    /// </summary>
    /// <remarks>
    /// Expecting defended decisions and stated limits of somebody's first piece of work is how a
    /// learner is told their work is inadequate for not being something nobody asked them to
    /// build.
    /// </remarks>
    [Fact]
    public void ABeginnerIsNotJudgedByAnAdvancedBar()
    {
        var beginner = Names(ProjectStandards.For("Beginner", SubmissionKind.Document));

        Assert.Contains("It is finished", beginner);
        Assert.DoesNotContain("The decisions are defended", beginner);
        Assert.DoesNotContain("The limits are stated", beginner);
    }

    /// <summary>
    /// And work meant for an employer is judged on judgement.
    /// </summary>
    [Fact]
    public void AnAdvancedProjectIsJudgedOnItsDecisions()
    {
        var advanced = Names(ProjectStandards.For("Advanced", SubmissionKind.Document));

        Assert.Contains("The decisions are defended", advanced);
        Assert.Contains("The limits are stated", advanced);
    }

    /// <summary>An unknown or missing stage is treated as the first one, not as the hardest.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something else")]
    public void AnUnknownStageIsJudgedGently(string? stage)
    {
        Assert.Contains("It is finished", Names(ProjectStandards.For(stage, SubmissionKind.Document)));
    }

    // ── What Radar actually had to judge ────────────────────────────────────

    /// <summary>
    /// An image or a video is judged on the learner's account, and the criterion says so.
    /// </summary>
    /// <remarks>
    /// The engine reads text and nothing else, so the artefact was never seen. Assessing the
    /// description is a reasonable thing to do and a dishonest thing to hide, which is why it is
    /// a named criterion on the same list rather than a caveat underneath one.
    /// </remarks>
    [Theory]
    [InlineData(SubmissionKind.Image)]
    [InlineData(SubmissionKind.Video)]
    public void AnUnreadableSubmissionSaysWhatWasActuallyJudged(SubmissionKind kind)
    {
        var criterion = ProjectStandards.For("Beginner", kind).Last();

        Assert.Equal("Your account of it", criterion.Name);
        Assert.Contains("cannot see", criterion.Asks);
        Assert.Contains("not the work itself", criterion.Asks);
    }

    /// <summary>A document is judged as the work, because it is the work.</summary>
    [Fact]
    public void AReadableSubmissionIsJudgedAsTheWork()
    {
        var criterion = ProjectStandards.For("Beginner", SubmissionKind.Document).Last();

        Assert.Equal("It is legible as submitted", criterion.Name);
        Assert.DoesNotContain("cannot see", criterion.Asks);
    }

    /// <summary>
    /// Every criterion asks something a reader could check, and none of them is empty.
    /// </summary>
    [Theory]
    [InlineData("Beginner")]
    [InlineData("Intermediate")]
    [InlineData("Advanced")]
    public void EveryCriterionAsksSomethingAnswerable(string stage)
    {
        foreach (var criterion in ProjectStandards.For(stage, SubmissionKind.Code))
        {
            Assert.False(string.IsNullOrWhiteSpace(criterion.Name));
            Assert.False(string.IsNullOrWhiteSpace(criterion.Asks));

            // A criterion is a question about the work, so it has to be long enough to be one.
            Assert.True(criterion.Asks.Length > 40, $"'{criterion.Name}' asks too little to answer.");
        }
    }

    private static List<string> Names(IEnumerable<AssessmentCriterion> criteria) =>
        [.. criteria.Select(c => c.Name)];
}

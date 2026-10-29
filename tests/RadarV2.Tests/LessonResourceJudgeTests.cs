using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Reconciling what the judge said with what was asked about.
///
/// The gate is only as trustworthy as this step. A verdict landing on the wrong item, or an item
/// being admitted because nothing was said about it, puts something unchecked in front of a
/// learner while every log line reports a successful judgement.
/// </summary>
public class LessonResourceJudgeTests
{
    private static LessonResourceJudge.Candidate Candidate(string id) =>
        new(id, $"Title {id}", "A source", "A summary");

    /// <summary>
    /// Matched by id, never by position.
    /// </summary>
    /// <remarks>
    /// A model that answers about three of four candidates, or answers in a different order, would
    /// otherwise shift every decision onto its neighbour — and the result reads as entirely
    /// normal. The same hazard the video summaries already guard against by echoing the id.
    /// </remarks>
    [Fact]
    public void AVerdictCannotLandOnTheWrongItem()
    {
        var candidates = new[] { Candidate("a"), Candidate("b"), Candidate("c") };

        // Answered out of order, and about only two of the three.
        var judged = new List<LessonResourceJudge.JudgedItem>
        {
            new() { Id = "c", Keep = true, Why = "teaches the thing" },
            new() { Id = "a", Keep = false, Why = "off topic" },
        };

        var kept = LessonResourceJudge.Reconcile(candidates, judged);

        Assert.Single(kept);
        Assert.Equal("c", kept[0].Id);
    }

    /// <summary>
    /// Saying nothing about an item is not approving it.
    /// </summary>
    /// <remarks>
    /// Silence is not consent when the question is what to put in front of somebody. Dropping an
    /// unmentioned item costs one reading; keeping it is the defect the whole gate exists to stop.
    /// </remarks>
    [Fact]
    public void AnItemTheJudgeIgnoredIsNotShown()
    {
        var candidates = new[] { Candidate("a"), Candidate("b") };

        var judged = new List<LessonResourceJudge.JudgedItem>
        {
            new() { Id = "a", Keep = true, Why = "on topic" },
        };

        var kept = LessonResourceJudge.Reconcile(candidates, judged);

        Assert.Single(kept);
        Assert.DoesNotContain(kept, v => v.Id == "b");
    }

    /// <summary>A verdict about something that was never offered is discarded.</summary>
    [Fact]
    public void AVerdictAboutAnUnknownItemIsIgnored()
    {
        var candidates = new[] { Candidate("a") };

        var judged = new List<LessonResourceJudge.JudgedItem>
        {
            new() { Id = "invented-by-the-model", Keep = true, Why = "excellent" },
        };

        Assert.Empty(LessonResourceJudge.Reconcile(candidates, judged));
    }

    [Fact]
    public void AnItemWithNoIdIsIgnored()
    {
        var candidates = new[] { Candidate("a") };

        var judged = new List<LessonResourceJudge.JudgedItem>
        {
            new() { Id = null, Keep = true, Why = "on topic" },
        };

        Assert.Empty(LessonResourceJudge.Reconcile(candidates, judged));
    }

    /// <summary>
    /// Kept without a reason is still kept, and nothing is invented to fill the line.
    /// </summary>
    /// <remarks>
    /// The card then shows the summary on its own. A sentence Radar wrote about why a resource
    /// matters, attributed to a judgement that did not produce one, is the kind of small
    /// fabrication the rest of the product refuses to make.
    /// </remarks>
    [Fact]
    public void AMissingReasonIsLeftEmptyRatherThanInvented()
    {
        var candidates = new[] { Candidate("a") };

        var judged = new List<LessonResourceJudge.JudgedItem>
        {
            new() { Id = "a", Keep = true, Why = null },
        };

        var kept = LessonResourceJudge.Reconcile(candidates, judged);

        Assert.Single(kept);
        Assert.Equal(string.Empty, kept[0].Why);
    }

    /// <summary>
    /// Dropping everything is a valid answer.
    /// </summary>
    /// <remarks>
    /// For a lesson like "Addition and Subtraction", a corpus of news and research genuinely holds
    /// nothing. An empty reading list is the correct result, and the lesson says so rather than
    /// showing the two least bad things it could find.
    /// </remarks>
    [Fact]
    public void KeepingNothingIsAnAnswer()
    {
        var candidates = new[] { Candidate("a"), Candidate("b") };

        var judged = new List<LessonResourceJudge.JudgedItem>
        {
            new() { Id = "a", Keep = false, Why = "reports an event, teaches nothing" },
            new() { Id = "b", Keep = false, Why = "different field, same words" },
        };

        Assert.Empty(LessonResourceJudge.Reconcile(candidates, judged));
    }
}

using System.Text.Json;
using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// What a stranger sees of a project somebody made public.
///
/// The endpoint returned the whole stored document to anybody with the id and no sign-in: every
/// submission, the full extracted text of the uploaded work, every assessment, and every
/// criterion with its evidence quoting that work — including the attempts that were refused. So
/// sharing a link to a finished project also published the three tries it took and what an
/// assessor said was wrong with each.
/// </summary>
public class PublicProjectTests
{
    private static StudioProject WithHistory() => new()
    {
        Id = "p1",
        UserId = "the-owner",
        Title = "Map your own network",
        Description = "A diagram of every device and open port.",
        IsCompleted = true,
        CompletedAt = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc),
        Submissions =
        [
            new ProjectSubmission
            {
                Kind = SubmissionKind.Document,
                ExtractedText = "SECRET: my home network has an open port on 192.168.0.4",
                Notes = "I was not sure about the subnet mask.",
                Assessment = new ProjectAssessment
                {
                    Score = 3,
                    IsAccepted = false,
                    Verdict = "Not accepted.",
                    Criteria = [new AssessmentCriterion
                    {
                        Name = "It is finished",
                        Verdict = "NotMet",
                        Evidence = "The diagram stops after two devices.",
                    }],
                },
            },
        ],
    };

    private static string Serialised(PublicProject view) =>
        JsonSerializer.Serialize(view, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    /// <summary>
    /// Nothing a learner handed in, and nothing an assessor said about it, leaves the account.
    /// </summary>
    [Fact]
    public void AStrangerSeesNothingThatWasSubmitted()
    {
        var json = Serialised(PublicProject.From(WithHistory()));

        Assert.DoesNotContain("SECRET", json);
        Assert.DoesNotContain("subnet mask", json);
        Assert.DoesNotContain("stops after two devices", json);
        Assert.DoesNotContain("submission", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nor the marks. That it met the standard is the useful public fact; what it scored, and
    /// which criteria it scraped through on, is between the learner and their assessment.
    /// </summary>
    [Fact]
    public void AStrangerSeesNoMarks()
    {
        var json = Serialised(PublicProject.From(WithHistory()));

        Assert.DoesNotContain("score", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("criteri", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verdict", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And nothing identifying whose it is.</summary>
    [Fact]
    public void AStrangerSeesNoOwner()
    {
        Assert.DoesNotContain("the-owner", Serialised(PublicProject.From(WithHistory())));
    }

    /// <summary>What a showcase is actually for still shows.</summary>
    [Fact]
    public void TheProjectItselfIsStillShown()
    {
        var view = PublicProject.From(WithHistory());

        Assert.Equal("Map your own network", view.Title);
        Assert.Equal("A diagram of every device and open port.", view.Description);
        Assert.True(view.IsAccepted);
        Assert.NotNull(view.CompletedAt);
    }

    /// <summary>
    /// An unfinished project does not claim to have been accepted.
    /// </summary>
    [Fact]
    public void AnUnfinishedProjectIsNotShownAsAccepted()
    {
        var unfinished = WithHistory();
        unfinished.IsCompleted = false;
        unfinished.CompletedAt = null;

        var view = PublicProject.From(unfinished);

        Assert.False(view.IsAccepted);
        Assert.Null(view.CompletedAt);
    }
}

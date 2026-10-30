using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Handing in work, and what happens to a project that was already accepted.
/// </summary>
/// <remarks>
/// ProjectScore covers the arithmetic, which is a pure function and well covered. What had no
/// test was the part that writes: whether acceptance is actually withdrawn in the database when
/// a revision falls short, and whether a project only its owner published is the only one a
/// stranger can read.
///
/// Both are the kind of thing that reads correctly and behaves otherwise - one is an update with
/// a field left out of it, the other a filter on one line.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class ProjectAcceptanceTests
{
    private readonly RadarDatabase _db;
    private readonly MongoProjectStudioService _projects;

    public ProjectAcceptanceTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("projects");
        _projects = new MongoProjectStudioService(_db, null!, null!);
    }

    private const string Owner = "the-owner";

    private async Task<StudioProject> Accepted()
    {
        var project = new StudioProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Owner,
            Title = "Map your own network",
            Description = "A diagram of every device.",
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow.AddDays(-1),
        };

        await _db.UserProjects.InsertOneAsync(project);
        return project;
    }

    private static ProjectSubmission Scoring(int score) => new()
    {
        Kind = SubmissionKind.Document,
        ExtractedText = "the work",
        Assessment = new ProjectAssessment
        {
            Score = score,
            IsAccepted = ProjectScore.IsAccepted(score),
            Criteria = [.. Enumerable.Range(0, 5).Select(_ => new AssessmentCriterion
            {
                Name = "A criterion",
                Verdict = score >= ProjectScore.PassMark ? "Met" : "NotMet",
            })],
        },
    };

    /// <summary>
    /// A revision that falls short takes the acceptance with it.
    /// </summary>
    /// <remarks>
    /// Acceptance was permanent once set: pass once, replace the work with anything, and the
    /// project stayed accepted - on the learner's list and on the public showcase page, which
    /// reads that flag and nothing else.
    /// </remarks>
    [Fact]
    public async Task AWorseRevisionWithdrawsTheAcceptance()
    {
        var project = await Accepted();

        await _projects.SubmitAsync(Owner, project.Id, Scoring(3));

        var stored = await _db.UserProjects.Find(p => p.Id == project.Id).FirstAsync();

        Assert.False(stored.IsCompleted);
        Assert.Null(stored.CompletedAt);

        // And the attempt itself is kept, because the point of a bar is the next try.
        Assert.Single(stored.Submissions);
    }

    /// <summary>A revision that still clears it keeps it.</summary>
    [Fact]
    public async Task ARevisionThatStillClearsTheBarKeepsIt()
    {
        var project = await Accepted();

        await _projects.SubmitAsync(Owner, project.Id, Scoring(9));

        var stored = await _db.UserProjects.Find(p => p.Id == project.Id).FirstAsync();

        Assert.True(stored.IsCompleted);
        Assert.NotNull(stored.CompletedAt);
    }

    /// <summary>Handing work in does not accept a project on its own.</summary>
    /// <remarks>
    /// Clearing the bar makes a project eligible; the learner still says when it is finished.
    /// </remarks>
    [Fact]
    public async Task ClearingTheBarDoesNotAcceptItByItself()
    {
        var project = await Accepted();
        project.IsCompleted = false;
        await _db.UserProjects.ReplaceOneAsync(p => p.Id == project.Id, project);

        await _projects.SubmitAsync(Owner, project.Id, Scoring(10));

        var stored = await _db.UserProjects.Find(p => p.Id == project.Id).FirstAsync();
        Assert.False(stored.IsCompleted);
    }

    /// <summary>Somebody else's project is not theirs to submit to.</summary>
    [Fact]
    public async Task AStrangerCannotSubmitToIt()
    {
        var project = await Accepted();

        Assert.Null(await _projects.SubmitAsync("somebody-else", project.Id, Scoring(9)));

        var stored = await _db.UserProjects.Find(p => p.Id == project.Id).FirstAsync();
        Assert.Empty(stored.Submissions);
        Assert.True(stored.IsCompleted);
    }

    /// <summary>
    /// Only a project its owner published is served to a stranger.
    /// </summary>
    /// <remarks>
    /// One filter on one line is the whole of what keeps private work off an endpoint that takes
    /// no sign-in, and until now nothing had asked the database whether it holds.
    /// </remarks>
    [Fact]
    public async Task OnlyAPublishedProjectIsPublic()
    {
        var private_ = await Accepted();

        Assert.Null(await _projects.GetPublicProjectAsync(private_.Id));

        await _projects.SetVisibilityAsync(Owner, private_.Id, isPublic: true);
        Assert.NotNull(await _projects.GetPublicProjectAsync(private_.Id));

        await _projects.SetVisibilityAsync(Owner, private_.Id, isPublic: false);
        Assert.Null(await _projects.GetPublicProjectAsync(private_.Id));
    }

    /// <summary>And publishing is the owner's call alone.</summary>
    [Fact]
    public async Task AStrangerCannotPublishIt()
    {
        var project = await Accepted();

        await _projects.SetVisibilityAsync("somebody-else", project.Id, isPublic: true);

        Assert.Null(await _projects.GetPublicProjectAsync(project.Id));
    }
}

using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using Microsoft.Extensions.Logging.Abstractions;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Switching between paths, and taking a section out of one.
/// </summary>
/// <remarks>
/// Neither had a test. Both write to the database, and the suite had nothing to write to - so
/// the two most destructive things a learner can do to their own path were covered by reading
/// the code and hoping.
///
/// What that cost: the switcher offered paths that the read side refuses, so choosing one
/// deactivated the working path and answered every later read with nothing. An empty Radar
/// Learn, a blank Today, and the screen that would have let them switch back was the one screen
/// that did not show the switcher.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class RoadmapSwitchingTests
{
    private readonly RadarDatabase _db;
    private readonly MongoRoadmapService _roadmaps;

    public RoadmapSwitchingTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("roadmaps");
        _roadmaps = Service(_db);
    }

    private const string Learner = "learner-1";
    private const string Somebody = "somebody-else";

    /// <summary>
    /// A path the learner built, which the read side will serve.
    /// </summary>
    /// <remarks>
    /// "user-built" is what WasAskedFor looks for. A path without it has to carry real progress
    /// to count, which is the distinction every test here turns on.
    /// </remarks>
    private async Task<GrowthRoadmap> Asked(string userId, bool active, string title = "Chosen")
    {
        var roadmap = new GrowthRoadmap
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Title = title,
            Interest = title,
            IsActive = active,
            TemplateKey = "user-built",
            Modules =
            [
                new RoadmapModule
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "First section",
                    Order = 1,
                    Level = "Beginner",
                    Lessons = [new RoadmapLesson { Id = Guid.NewGuid().ToString(), Title = "A lesson", Order = 1 }],
                },
                new RoadmapModule
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Second section",
                    Order = 2,
                    Level = "Beginner",
                    Lessons = [new RoadmapLesson { Id = Guid.NewGuid().ToString(), Title = "Another", Order = 1 }],
                },
            ],
        };

        await _db.Roadmaps.InsertOneAsync(roadmap);
        return roadmap;
    }

    /// <summary>A path generated before anybody was asked, with no work on it.</summary>
    private async Task<GrowthRoadmap> NeverAskedFor(string userId)
    {
        var roadmap = new GrowthRoadmap
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Title = "Foundations of Computer Science",
            Interest = "Computer Science",
            IsActive = false,
            TemplateKey = "generated",
            Modules = [new RoadmapModule { Id = Guid.NewGuid().ToString(), Title = "Generated", Order = 1 }],
        };

        await _db.Roadmaps.InsertOneAsync(roadmap);
        return roadmap;
    }

    private async Task<int> ActiveCount(string userId) =>
        (int)await _db.Roadmaps.CountDocumentsAsync(r => r.UserId == userId && r.IsActive);

    // ── Switching ───────────────────────────────────────────────────────────

    /// <summary>Choosing another path makes it the one they are on, and only it.</summary>
    [Fact]
    public async Task SwitchingLeavesExactlyOneActivePath()
    {
        var current = await Asked(Learner, active: true, "Prompt engineering");
        var other = await Asked(Learner, active: false, "Public health");

        var switched = await _roadmaps.ActivateRoadmapAsync(Learner, other.Id);

        Assert.NotNull(switched);
        Assert.Equal(other.Id, switched!.Id);
        Assert.Equal(1, await ActiveCount(Learner));

        var active = await _db.Roadmaps.Find(r => r.UserId == Learner && r.IsActive).FirstAsync();
        Assert.Equal(other.Id, active.Id);
        Assert.NotEqual(current.Id, active.Id);
    }

    /// <summary>
    /// A path the reader cannot load is refused rather than quietly switched to.
    /// </summary>
    /// <remarks>
    /// The failure this is here for. Activating one deactivated the working path and then every
    /// read answered with nothing, because a generated path with no progress on it reads as
    /// absent - so the learner was left with no path at all and no way back to the one they had.
    /// </remarks>
    [Fact]
    public async Task APathThatWouldNotLoadIsRefused()
    {
        var working = await Asked(Learner, active: true);
        var generated = await NeverAskedFor(Learner);

        var refused = await _roadmaps.ActivateRoadmapAsync(Learner, generated.Id);

        Assert.Null(refused);

        // And the path they were on is untouched.
        Assert.Equal(1, await ActiveCount(Learner));
        var still = await _db.Roadmaps.Find(r => r.UserId == Learner && r.IsActive).FirstAsync();
        Assert.Equal(working.Id, still.Id);
    }

    /// <summary>Somebody else's path is not theirs to switch to.</summary>
    [Fact]
    public async Task APathBelongingToSomebodyElseIsNotFound()
    {
        await Asked(Learner, active: true);
        var theirs = await Asked(Somebody, active: true);

        Assert.Null(await _roadmaps.ActivateRoadmapAsync(Learner, theirs.Id));

        // Still active for its owner.
        Assert.Equal(1, await ActiveCount(Somebody));
    }

    /// <summary>The switcher only offers paths that survive being chosen.</summary>
    [Fact]
    public async Task OnlySwitchablePathsAreOffered()
    {
        var built = await Asked(Learner, active: true);
        var generated = await NeverAskedFor(Learner);

        var offered = (await _roadmaps.GetSwitchableRoadmapsAsync(Learner)).Select(r => r.Id).ToList();

        Assert.Contains(built.Id, offered);
        Assert.DoesNotContain(generated.Id, offered);
    }

    /// <summary>Switching to the path already active is not a way to end up with none.</summary>
    [Fact]
    public async Task SwitchingToTheCurrentPathKeepsIt()
    {
        var current = await Asked(Learner, active: true);

        Assert.NotNull(await _roadmaps.ActivateRoadmapAsync(Learner, current.Id));
        Assert.Equal(1, await ActiveCount(Learner));
    }

    // ── Removing a section ──────────────────────────────────────────────────

    /// <summary>A removed section is gone, and what is left is renumbered.</summary>
    /// <remarks>
    /// Order is what the screen sorts by, so a gap in it reads as a missing lesson.
    /// </remarks>
    [Fact]
    public async Task RemovingASectionRenumbersTheRest()
    {
        var path = await Asked(Learner, active: true);
        var first = path.Modules[0].Id;
        var second = path.Modules[1].Id;

        var updated = await _roadmaps.RemoveSectionAsync(Learner, path.Id, first);

        Assert.NotNull(updated);
        Assert.DoesNotContain(updated!.Modules, m => m.Id == first);
        Assert.Equal(second, Assert.Single(updated.Modules).Id);
        Assert.Equal(1, updated.Modules[0].Order);

        // And it is gone from the database, not only from the object handed back.
        var stored = await _db.Roadmaps.Find(r => r.Id == path.Id).FirstAsync();
        Assert.Single(stored.Modules);
    }

    /// <summary>Somebody else's path is not theirs to edit.</summary>
    [Fact]
    public async Task ASectionOfSomebodyElsesPathIsNotRemovable()
    {
        var theirs = await Asked(Somebody, active: true);

        Assert.Null(await _roadmaps.RemoveSectionAsync(Learner, theirs.Id, theirs.Modules[0].Id));

        var stored = await _db.Roadmaps.Find(r => r.Id == theirs.Id).FirstAsync();
        Assert.Equal(2, stored.Modules.Count);
    }

    /// <summary>Removing the last section leaves a path, not a hole.</summary>
    /// <remarks>
    /// The learner lands on the empty screen, which is the one that has to offer a way back.
    /// What it must not do is lose the path, because the path is what the switcher switches from.
    /// </remarks>
    [Fact]
    public async Task RemovingTheLastSectionLeavesThePathStanding()
    {
        var path = await Asked(Learner, active: true);

        foreach (var module in path.Modules.ToList())
        {
            await _roadmaps.RemoveSectionAsync(Learner, path.Id, module.Id);
        }

        var stored = await _db.Roadmaps.Find(r => r.Id == path.Id).FirstAsync();
        Assert.Empty(stored.Modules);
        Assert.True(stored.IsActive);

        // And the read side still accepts it, because the learner built it.
        Assert.True(MongoRoadmapService.WasAskedFor(stored));
    }

    /// <summary>A section that is not there is not an error, and changes nothing.</summary>
    [Fact]
    public async Task RemovingASectionThatIsNotThereChangesNothing()
    {
        var path = await Asked(Learner, active: true);

        var updated = await _roadmaps.RemoveSectionAsync(Learner, path.Id, "no-such-module");

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Modules.Count);
    }

    /// <summary>
    /// The service with only the collaborator these methods use.
    /// </summary>
    /// <remarks>
    /// Switching, listing and removing a section touch the database and nothing else - no model,
    /// no video search, no learning record. Passing null for the rest says exactly that, and
    /// fails loudly if one of them ever starts reaching further, which is more use than a graph
    /// of stubs that quietly absorbs it.
    ///
    /// GetActiveRoadmapAsync is deliberately not called here: it rebuilds, personalises and
    /// enriches, which is a different test with a different set of collaborators.
    /// </remarks>
    private static MongoRoadmapService Service(RadarDatabase db) =>
        new(db, null!, null!, null!, null!, NullLogger<MongoRoadmapService>.Instance);
}

using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// An old roadmap document must not be able to take a page down.
///
/// The C# model defaults every collection to empty, so this looks impossible until you meet a
/// document written with an explicit null. The production database is the oldest and has them.
/// `WasAskedFor` is reached from `GetActiveRoadmapAsync`, which Today, Opportunities and the
/// Mentor all depend on — so one such document broke three screens at once, and the Opportunities
/// page showed "Could not load opportunities" rather than loading them.
/// </summary>
public class RoadmapNullSafetyTests
{
    [Fact]
    public void SurvivesARoadmapWhoseCollectionsAreNull()
    {
        var roadmap = new GrowthRoadmap
        {
            TemplateKey = "generic",
            Modules = null!,
            CompletedResourceIds = null!,
            CompletedExerciseIds = null!,
            QuizResults = null!,
        };

        // Asserting it does not throw is the point; the value is secondary.
        Assert.False(MongoRoadmapService.WasAskedFor(roadmap));
    }

    [Fact]
    public void SurvivesAModuleWhoseLessonsAreNull()
    {
        var roadmap = new GrowthRoadmap
        {
            TemplateKey = "generic",
            Modules = [new RoadmapModule { Title = "Old module", Lessons = null! }],
        };

        Assert.False(MongoRoadmapService.WasAskedFor(roadmap));
    }

    // Null collections must not be mistaken for progress either — the answer has to stay correct,
    // not merely avoid throwing.
    [Fact]
    public void StillRecognisesRealProgressAlongsideNullCollections()
    {
        var roadmap = new GrowthRoadmap
        {
            TemplateKey = "generic",
            Modules = [new RoadmapModule { Title = "Worked through", Lessons = [new RoadmapLesson { IsCompleted = true }] }],
            CompletedResourceIds = null!,
            QuizResults = null!,
        };

        Assert.True(MongoRoadmapService.WasAskedFor(roadmap));
    }

    [Fact]
    public void StillRecognisesALearnerBuiltPathWithNullCollections()
    {
        var roadmap = new GrowthRoadmap
        {
            TemplateKey = MongoRoadmapService.UserBuiltTemplateKey,
            Modules = null!,
        };

        Assert.True(MongoRoadmapService.WasAskedFor(roadmap));
    }
}

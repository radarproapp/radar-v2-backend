using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A path generated from someone's degree before they were asked is not their path.
///
/// Stopping the generator did nothing for anyone who already had one of its roadmaps stored —
/// which was every existing user. They went on opening Radar Learn to "Foundations of Computer
/// Science" and twelve lessons they never chose. An unasked-for path now reads as absent, so they
/// get the empty state and the three questions.
///
/// The exception that matters: a generated path somebody has actually been working through is
/// theirs by use, and hiding it would take their progress off the screen.
/// </summary>
public class PathWasAskedForTests
{
    private static GrowthRoadmap Generated() => new()
    {
        TemplateKey = "generic",
        Title = "Computer Science",
        Modules =
        [
            new RoadmapModule
            {
                Title = "Beginner: Foundations of Computer Science",
                Level = "Beginner",
                Lessons =
                [
                    new RoadmapLesson { Title = "What Computer Science actually involves day to day" },
                    new RoadmapLesson { Title = "Core concepts and vocabulary in Computer Science" },
                ],
            },
        ],
    };

    [Fact]
    public void AGeneratedPathNobodyTouchedWasNotAskedFor()
    {
        Assert.False(MongoRoadmapService.WasAskedFor(Generated()));
    }

    [Fact]
    public void APathBuiltFromTheQuestionsWasAskedFor()
    {
        var roadmap = new GrowthRoadmap
        {
            TemplateKey = MongoRoadmapService.UserBuiltTemplateKey,
            Modules = [new RoadmapModule { Title = "Cybersecurity" }],
        };

        Assert.True(MongoRoadmapService.WasAskedFor(roadmap));
    }

    // Made theirs by working through it.
    [Fact]
    public void AGeneratedPathWithAFinishedLessonIsKept()
    {
        var roadmap = Generated();
        roadmap.Modules[0].Lessons[0].IsCompleted = true;

        Assert.True(MongoRoadmapService.WasAskedFor(roadmap));
    }

    [Fact]
    public void AGeneratedPathWithOtherProgressIsKept()
    {
        var withResource = Generated();
        withResource.CompletedResourceIds.Add("lp-beginner-01");
        Assert.True(MongoRoadmapService.WasAskedFor(withResource));

        var withExercise = Generated();
        withExercise.CompletedExerciseIds.Add("lp-beginner-01");
        Assert.True(MongoRoadmapService.WasAskedFor(withExercise));

        var withQuiz = Generated();
        withQuiz.QuizResults.Add(new QuizPerformanceRecord { Topic = "Networking", Stage = "Beginner", ScorePercent = 80 });
        Assert.True(MongoRoadmapService.WasAskedFor(withQuiz));
    }

    [Fact]
    public void AGeneratedPathWithACompletedModuleIsKept()
    {
        var roadmap = Generated();
        roadmap.Modules[0].IsCompleted = true;

        Assert.True(MongoRoadmapService.WasAskedFor(roadmap));
    }

    // A percentage the server computed is not evidence the learner did anything.
    [Fact]
    public void AProgressPercentAloneDoesNotCountAsHavingAskedForIt()
    {
        var roadmap = Generated();
        roadmap.ProgressPercent = 40;

        Assert.False(MongoRoadmapService.WasAskedFor(roadmap));
    }
}

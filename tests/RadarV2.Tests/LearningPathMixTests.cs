using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Spec 5.3 (80% how-to / 20% current developments) and 6.2 (a mix of formats).
///
/// The ratio was already right and nothing checked it, which means it could drift without anyone
/// noticing. The mix was not: a path was videos and only videos, which for a product whose point
/// is "building skills and demonstration of capabilities by learning and building projects at the
/// same time" left out the half that demonstrates anything.
/// </summary>
public class LearningPathMixTests
{
    private static GrowthRoadmap Roadmap(int lessonCount) => new()
    {
        Goal = "Become a SOC analyst",
        Interest = "Cybersecurity",
        Modules =
        [
            new RoadmapModule
            {
                Level = "Beginner",
                Title = "Foundations",
                Project = new StageProject
                {
                    Title = "Map your own network",
                    Deliverable = "a one page diagram of every device on your network",
                    WhyItProvesCapability = "It shows you can find what is really on a network.",
                    EstimatedTime = "2 hours",
                    Steps = ["Scan the network", "Draw the connections"],
                },
                Lessons = Enumerable.Range(1, lessonCount)
                    .Select(i => new RoadmapLesson { Order = i, Title = $"Lesson {i}", EstimatedTime = "30 min" })
                    .ToList(),
            },
        ],
    };

    [Fact]
    public void A_path_is_not_only_things_to_watch()
    {
        var resources = LearningPathResourceBuilder.Build(Roadmap(4), null);

        Assert.Contains(resources, r => r.Type == LearningResourceType.Project);
    }

    [Fact]
    public void The_project_carries_what_the_learner_will_have_at_the_end()
    {
        var project = LearningPathResourceBuilder.Build(Roadmap(4), null)
            .First(r => r.Type == LearningResourceType.Project);

        Assert.Contains("one page diagram", project.WhatYouWillBuild);
        Assert.Contains("really on a network", project.WhyAmILearningThis);
        Assert.Equal("Build", project.SequenceRole);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Roughly_four_in_five_items_are_how_to_material(int lessonCount)
    {
        // Spec 5.3. Exactly 80/20 is not achievable at every length, so this asserts the shape:
        // tutorials dominate, and there is exactly one current-developments item per module.
        var resources = LearningPathResourceBuilder.Build(Roadmap(lessonCount), null);

        var tutorials = resources.Count(r => r.Type == LearningResourceType.TutorialVideo);
        var developments = resources.Count(r => r.Type == LearningResourceType.NewDevelopmentVideo);

        Assert.Equal(lessonCount, tutorials);
        Assert.Equal(1, developments);
        Assert.True(tutorials >= developments * 3, "how-to material should dominate the stage");
    }

    [Fact]
    public void A_module_with_no_project_still_builds_a_path()
    {
        var roadmap = Roadmap(3);
        roadmap.Modules[0].Project = null;

        var resources = LearningPathResourceBuilder.Build(roadmap, null);

        Assert.DoesNotContain(resources, r => r.Type == LearningResourceType.Project);
        Assert.NotEmpty(resources);
    }
}

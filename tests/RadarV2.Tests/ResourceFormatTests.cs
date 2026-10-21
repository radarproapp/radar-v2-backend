using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Spec 5.5: how-to material reads as steps; articles and current-developments items read as what
/// happened. The two formats answer different questions, and a reader can tell within a line which
/// one they are looking at — so putting the wrong one on a resource is worse than giving it none.
///
/// The hand-written catalogue already got this right (34 tutorials with steps, 9 articles and
/// development videos without) and nothing checked it, which is the usual way a convention rots.
/// </summary>
public class ResourceFormatTests
{
    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("Data Analytics")]
    public void How_to_material_reads_as_steps(string subject)
    {
        var tutorials = LearningResourceCatalog.GetResourcesForSkill(subject)
            .Where(r => r.Type == LearningResourceType.TutorialVideo)
            .ToList();

        Assert.NotEmpty(tutorials);
        Assert.All(tutorials, r =>
            Assert.True(r.StepByStepBreakdown.Count > 0, $"'{r.Title}' is a tutorial with no steps"));
    }

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("Data Analytics")]
    public void Current_developments_do_not_pretend_to_be_tutorials(string subject)
    {
        var articles = LearningResourceCatalog.GetResourcesForSkill(subject)
            .Where(r => r.Type is LearningResourceType.NewDevelopmentVideo or LearningResourceType.BlogPost)
            .ToList();

        Assert.All(articles, r =>
            Assert.True(r.StepByStepBreakdown.Count == 0,
                $"'{r.Title}' is a {r.Type} carrying a step-by-step breakdown"));
    }

    [Fact]
    public void A_stage_project_carries_its_real_steps()
    {
        var roadmap = new GrowthRoadmap
        {
            Goal = "Become a SOC analyst",
            Modules =
            [
                new RoadmapModule
                {
                    Level = "Beginner",
                    Title = "Foundations",
                    Project = new StageProject
                    {
                        Title = "Map your own network",
                        Deliverable = "a one page diagram",
                        EstimatedTime = "2 hours",
                        Steps = ["Scan the network", "Draw the connections", "Label each device"],
                    },
                    Lessons = [new RoadmapLesson { Order = 1, Title = "Networking basics" }],
                },
            ],
        };

        var project = LearningPathResourceBuilder.Build(roadmap, null)
            .First(r => r.Type == LearningResourceType.Project);

        Assert.Equal(3, project.StepByStepBreakdown.Count);
        Assert.Equal("Scan the network", project.StepByStepBreakdown[0].Instruction);
        Assert.Equal(1, project.StepByStepBreakdown[0].StepNumber);
    }

    [Fact]
    public void A_video_nobody_watched_gets_no_invented_steps()
    {
        // The honest limit. Radar has not seen these videos, so it cannot say what their steps
        // are; writing them anyway is the fabrication this codebase keeps removing. Real steps
        // for videos wait on transcripts.
        var roadmap = new GrowthRoadmap
        {
            Goal = "Become a SOC analyst",
            Modules = [new RoadmapModule { Level = "Beginner", Lessons = [new RoadmapLesson { Order = 1, Title = "Networking basics" }] }],
        };

        var videos = LearningPathResourceBuilder.Build(roadmap, null)
            .Where(r => r.Type == LearningResourceType.TutorialVideo)
            .ToList();

        Assert.NotEmpty(videos);
        Assert.All(videos, v => Assert.Empty(v.StepByStepBreakdown));
    }
}

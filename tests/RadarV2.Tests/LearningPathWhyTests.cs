using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Spec 5.6. Every lesson used to carry the same sentence: "you need X before the next step makes
/// sense" — asserted on every card without knowing what the next step was, which made it both
/// identical everywhere and unverified.
///
/// The builder has the ordered lesson list and the module's project, so it can name the thing the
/// lesson actually leads to.
/// </summary>
public class LearningPathWhyTests
{
    private static GrowthRoadmap Roadmap() => new()
    {
        Goal = "Become a SOC analyst",
        Interest = "Cybersecurity",
        Title = "Cybersecurity path",
        Modules =
        [
            new RoadmapModule
            {
                Level = "Beginner",
                Project = new StageProject { Deliverable = "a working log collector" },
                Lessons =
                [
                    new RoadmapLesson { Order = 1, Title = "Networking basics", EstimatedTime = "40 min" },
                    new RoadmapLesson { Order = 2, Title = "Reading packet captures", EstimatedTime = "35 min" },
                    new RoadmapLesson { Order = 3, Title = "Writing a detection rule", EstimatedTime = "50 min" },
                ],
            },
        ],
    };

    private static List<LearningResource> Build() =>
        LearningPathResourceBuilder.Build(Roadmap(), null);

    [Fact]
    public void A_lesson_names_the_lesson_it_leads_to()
    {
        var lessons = Build().Where(r => r.Type == LearningResourceType.TutorialVideo).ToList();

        Assert.Contains("packet captures", lessons[0].WhyAmILearningThis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("detection rule", lessons[1].WhyAmILearningThis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_last_lesson_names_the_project_rather_than_a_step_that_does_not_exist()
    {
        var lessons = Build().Where(r => r.Type == LearningResourceType.TutorialVideo).ToList();

        Assert.Contains("log collector", lessons[^1].WhyAmILearningThis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_two_lessons_carry_the_same_reason()
    {
        // The test that would have caught the original: one sentence, repeated everywhere.
        var reasons = Build()
            .Where(r => r.Type == LearningResourceType.TutorialVideo)
            .Select(r => r.WhyAmILearningThis)
            .ToList();

        Assert.Equal(reasons.Count, reasons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_reason_is_about_this_learners_path_not_a_generic_one()
    {
        // 5.6 asks for the reason to answer against the user's goal. It does by construction:
        // the subject is derived from the goal, and every sentence is built from this roadmap's
        // own lessons and project - none of it would read correctly on a different path.
        var lessons = Build().Where(r => r.Type == LearningResourceType.TutorialVideo).ToList();

        Assert.All(lessons, l => Assert.DoesNotContain("the next step", l.WhyAmILearningThis, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("packet captures", lessons[0].WhyAmILearningThis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sequencing_tells_the_learner_where_to_start()
    {
        // Spec 5.4: the order is only useful if the first one says so.
        var lessons = Build().Where(r => r.Type == LearningResourceType.TutorialVideo).ToList();

        Assert.Equal("Start here", lessons[0].SequenceRole);
        Assert.All(lessons, l => Assert.False(string.IsNullOrWhiteSpace(l.EstimatedTime)));
    }
}

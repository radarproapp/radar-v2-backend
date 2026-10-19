using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Applies a <see cref="UserLearningRecord"/> to a roadmap, so two people with the same goal and
/// different histories get different paths.
///
/// Before this, <c>BuildFromTemplate</c> read one string — the goal — and everyone with that goal
/// got byte-identical modules starting at lesson one. Everything Radar had observed about the user
/// sat unused in another collection.
///
/// Runs on creation *and* on read, because evidence keeps arriving: a user who fails a topic twice
/// should see that topic again, and one who proves a stage should not be held at it.
///
/// Conservative by design. When <see cref="UserLearningRecord.Confidence"/> is low, tailoring stays
/// close to what the user declared — inventing a bespoke path out of two data points is worse than
/// following their own stated starting point.
/// </summary>
public static class RoadmapPersonalizer
{
    /// <summary>Below this, Radar does not know enough to override what the user told us.</summary>
    private const double MinConfidenceToReshape = 0.35;

    public static void Apply(GrowthRoadmap roadmap, UserLearningRecord record)
    {
        AttachStageProjects(roadmap, record);

        if (record.Confidence < MinConfidenceToReshape)
        {
            // Still honour the declared starting stage, just don't infer beyond it.
            AlignStage(roadmap, record.DeclaredLevel);
            return;
        }

        AlignStage(roadmap, record.RecommendedStartStage);
        MarkMasteredTopics(roadmap, record);
        SurfaceWeakAreas(roadmap, record);
        roadmap.AdaptiveRecommendation = BuildRecommendation(record);
    }

    /// <summary>
    /// Moves the roadmap to the stage the evidence supports and unlocks everything up to it, so a
    /// user with proven skill is not forced to walk through material they have already shown.
    /// </summary>
    private static void AlignStage(GrowthRoadmap roadmap, string stage)
    {
        if (string.IsNullOrWhiteSpace(stage)) return;

        var order = new[] { "Beginner", "Intermediate", "Advanced", "Application" };
        var target = Array.FindIndex(order, s => s.Equals(stage, StringComparison.OrdinalIgnoreCase));
        if (target < 0) return;

        roadmap.CurrentStage = order[target];

        foreach (var module in roadmap.Modules)
        {
            var index = Array.FindIndex(order, s => s.Equals(module.Level, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            module.IsLocked = index > target;
        }
    }

    /// <summary>Topics the user has already demonstrated are marked done rather than re-taught.</summary>
    private static void MarkMasteredTopics(GrowthRoadmap roadmap, UserLearningRecord record)
    {
        if (record.MasteredTopics.Count == 0) return;

        foreach (var module in roadmap.Modules)
        {
            foreach (var lesson in module.Lessons)
            {
                if (lesson.IsCompleted) continue;
                if (record.MasteredTopics.Any(t => Mentions(lesson.Title, t)))
                    lesson.IsCompleted = true;
            }

            if (module.Lessons.Count > 0 && module.Lessons.All(l => l.IsCompleted))
                module.IsCompleted = true;
        }
    }

    /// <summary>
    /// Adds extra practice for anything the user is struggling with, at the front of their current
    /// stage. Tracking struggle is only worth it if something changes as a result.
    /// </summary>
    private static void SurfaceWeakAreas(GrowthRoadmap roadmap, UserLearningRecord record)
    {
        if (record.WeakAreas.Count == 0) return;

        var current = roadmap.Modules.FirstOrDefault(m =>
            m.Level.Equals(roadmap.CurrentStage, StringComparison.OrdinalIgnoreCase));
        if (current is null) return;

        foreach (var weak in record.WeakAreas.Take(2))
        {
            var title = $"Extra practice: {weak}";
            if (current.Lessons.Any(l => l.Title.Equals(title, StringComparison.OrdinalIgnoreCase))) continue;

            current.Lessons.Insert(0, new RoadmapLesson
            {
                Title = title,
                Body = $"You got {weak} wrong more than once. Go over it again before moving on.",
                Order = 0,
                EstimatedTime = "25 min",
            });
        }

        for (var i = 0; i < current.Lessons.Count; i++)
            current.Lessons[i].Order = i + 1;
    }

    /// <summary>
    /// Gives every stage something to build. Projects come from StageProjectCatalog, which keeps
    /// domains as data — so adding one is one entry, not another keyword branch in three files.
    /// </summary>
    private static void AttachStageProjects(GrowthRoadmap roadmap, UserLearningRecord record)
    {
        // Goal first, interest only as a fallback: the goal is what the learner actually typed, and
        // gluing both together produced titles like "Become a hospital nurse manager Nursing".
        var subject = SubjectPhrase.Clean(
            !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : roadmap.Interest,
            record.PrimaryGoal);

        foreach (var module in roadmap.Modules)
        {
            module.Project ??= StageProjectCatalog.For(module.Level, subject, record.Capabilities.FirstOrDefault());
        }
    }

    private static string BuildRecommendation(UserLearningRecord record)
    {
        if (record.WeakAreas.Count > 0)
            return $"Radar put extra practice on {record.WeakAreas[0]} at the top of your stage, because you got it wrong more than once.";

        if (record.MasteredTopics.Count > 0)
            return $"Radar marked {record.MasteredTopics[0]} as done, because you already passed it. You can move on.";

        return $"Radar is starting you at the {record.RecommendedStartStage} stage, based on what you told us and what you have done so far.";
    }

    private static bool Mentions(string? text, string term) =>
        !string.IsNullOrWhiteSpace(text)
        && !string.IsNullOrWhiteSpace(term)
        && text.Contains(term, StringComparison.OrdinalIgnoreCase);
}

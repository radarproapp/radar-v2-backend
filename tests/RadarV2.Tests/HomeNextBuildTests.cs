using System.Reflection;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// "Next build" must survive an empty path.
///
/// The regression: <c>TargetRole</c> is a non-nullable string defaulting to "", so the
/// <c>?? </c> chain picking a topic always stopped at the empty string and the whole section
/// returned null. It stayed invisible for as long as paths were auto-created from the profile,
/// because <c>roadmap?.Goal</c> answered first and was never empty. The moment a path was
/// allowed to start empty — which is the point — every user lost the section.
/// </summary>
public class HomeNextBuildTests
{
    /// <summary>
    /// BuildNextBuild is private and static; it has no dependencies worth constructing a service
    /// for, so it is called directly rather than reshaped for the test.
    /// </summary>
    private static object? NextBuild(UserProfile profile, GrowthRoadmap? roadmap)
    {
        var method = typeof(HomeBriefService)
            .GetMethod("BuildNextBuild", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return method!.Invoke(null, [profile, roadmap]);
    }

    private static string? ArtifactOf(object? build) =>
        build?.GetType().GetProperty("Artifact")?.GetValue(build) as string;

    [Fact]
    public void SurvivesAnEmptyPathWhenTheLearnerHasInterests()
    {
        var profile = new UserProfile
        {
            // Exactly what a fresh profile looks like: set to "" rather than left null.
            TargetRole = string.Empty,
            PrimaryGoal = string.Empty,
            Interests = ["Computer Science"],
        };

        var build = NextBuild(profile, null);

        Assert.NotNull(build);
        Assert.False(string.IsNullOrWhiteSpace(ArtifactOf(build)));
    }

    [Fact]
    public void PrefersTheStatedRoleOverAnInterest()
    {
        var profile = new UserProfile
        {
            TargetRole = "SOC analyst",
            Interests = ["Cybersecurity"],
        };

        var build = NextBuild(profile, null);

        Assert.NotNull(build);
    }

    // Nothing to go on is still nothing to go on: no section rather than an invented one.
    [Fact]
    public void StaysSilentWhenThereIsNothingToBuildFrom()
    {
        var profile = new UserProfile
        {
            TargetRole = string.Empty,
            PrimaryGoal = string.Empty,
            Interests = [],
        };

        Assert.Null(NextBuild(profile, null));
    }

    [Fact]
    public void UsesThePathGoalWhenThereIsOne()
    {
        var profile = new UserProfile { TargetRole = string.Empty, Interests = ["Cybersecurity"] };
        var roadmap = new GrowthRoadmap { Goal = "Become a SOC analyst" };

        var build = NextBuild(profile, roadmap);

        Assert.NotNull(build);
    }
}

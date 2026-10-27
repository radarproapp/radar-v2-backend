using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// Personalisation is a hard requirement: the feed does not populate until it is answered.
///
/// Whether someone has personalised is derived rather than stored. A flag would need a migration
/// and would lie about anyone who completed the older onboarding, which never asked for goals —
/// they would read as personalised with box 2 empty.
/// </summary>
public class PersonalisationTests
{
    [Fact]
    public void ANewProfileIsNotPersonalised()
    {
        Assert.False(new UserProfile().IsPersonalised);
    }

    // The case a stored flag would get wrong: the older onboarding collected interests and a
    // single goal string, never the goals list.
    [Fact]
    public void InterestsAloneAreNotEnough()
    {
        var profile = new UserProfile
        {
            Interests = ["Computer Science"],
            PrimaryGoal = "Computer Science",
        };

        Assert.False(profile.IsPersonalised);
    }

    [Fact]
    public void GoalsAloneAreNotEnough()
    {
        Assert.False(new UserProfile { Goals = ["Find a job"] }.IsPersonalised);
    }

    [Fact]
    public void OneOfEachIsEnough()
    {
        var profile = new UserProfile
        {
            Interests = ["Software Engineering"],
            Goals = ["Find a job"],
        };

        Assert.True(profile.IsPersonalised);
    }

    // Asked for, never required. Someone who wants nothing applied for should still get a feed.
    [Fact]
    public void OpportunityPreferencesAreOptional()
    {
        var profile = new UserProfile
        {
            Interests = ["Nursing"],
            Goals = ["Improve academically"],
            OpportunityPreferences = [],
        };

        Assert.True(profile.IsPersonalised);
    }

    // Free text arrives from the three boxes; whitespace is not an answer.
    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void BlankAnswersDoNotCount(string blank)
    {
        var profile = new UserProfile
        {
            Interests = ["Law"],
            Goals = [blank],
        };

        Assert.False(profile.IsPersonalised);
    }
}

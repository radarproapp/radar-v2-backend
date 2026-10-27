using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Box 2 of personalisation collects a list, and the whole list has to count.
///
/// Personalisation is a hard requirement — the feed does not populate until it is answered — so
/// an answer that changes nothing is worse than not asking. The intent reader took only
/// PrimaryGoal, which meant "learn a new skill, get a promotion" resolved to LearnSubject and
/// promotion-shaped opportunities never lifted for someone who had said so in as many words.
/// </summary>
public class GoalListIntentTests
{
    private static UserProfile WithGoals(params string[] goals) => new()
    {
        Persona = PersonaType.YoungProfessional,
        Interests = ["Software Engineering"],
        Goals = [.. goals],
    };

    [Fact]
    public void ReadsAnOutcomeStatedInTheFirstGoal()
    {
        Assert.Equal(LearningOutcome.NewJob, LearningIntentReader.Read(WithGoals("Find a job")).Outcome);
    }

    // The case that was silently dropped.
    [Fact]
    public void ReadsAnOutcomeStatedInALaterGoal()
    {
        var intent = LearningIntentReader.Read(WithGoals("Learn a new skill", "Get a promotion"));

        Assert.Equal(LearningOutcome.Promotion, intent.Outcome);
    }

    // A list of goals must not be concatenated into the subject line: it is shown in the roadmap,
    // where "Find a job. Learn a new skill" reads as a bug.
    [Fact]
    public void KeepsTheSubjectReadableWhenThereAreSeveralGoals()
    {
        var intent = LearningIntentReader.Read(WithGoals("Learn a new skill", "Get a promotion", "Study abroad"));

        Assert.DoesNotContain(". ", intent.Subject);
        Assert.False(string.IsNullOrWhiteSpace(intent.Subject));
    }

    // Profiles written by the older onboarding have a PrimaryGoal and no list at all.
    [Fact]
    public void FallsBackToPrimaryGoalWhenTheListIsEmpty()
    {
        var profile = new UserProfile
        {
            Persona = PersonaType.YoungProfessional,
            Interests = ["Software Engineering"],
            PrimaryGoal = "Get a promotion",
        };

        Assert.Equal(LearningOutcome.Promotion, LearningIntentReader.Read(profile).Outcome);
    }

    // PrimaryGoal is what the roadmap is built around, so when it names an outcome it still wins.
    [Fact]
    public void PrimaryGoalStillLeadsWhenItNamesAnOutcome()
    {
        var profile = WithGoals("Learn a new skill", "Find a job");
        profile.PrimaryGoal = "Start a business";

        Assert.Equal(LearningOutcome.StartBusiness, LearningIntentReader.Read(profile).Outcome);
    }

    [Fact]
    public void NoGoalAtAllIsStillReadable()
    {
        var intent = LearningIntentReader.Read(new UserProfile { Interests = ["Nursing"] });

        Assert.Equal(LearningOutcome.LearnSubject, intent.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(intent.Subject));
    }
}

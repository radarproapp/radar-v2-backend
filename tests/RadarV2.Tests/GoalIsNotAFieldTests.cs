using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A goal is an outcome. It is not the field somebody works in, and it is not a search term.
/// </summary>
/// <remarks>
/// Reported from production. A learner whose stated interest is International Relations opened
/// Opportunities and was shown three senior AI engineering roles, each at ninety per cent, each
/// carrying the same two sentences: "It is a job work, which is what you are learning." and
/// "It fits what you said you are working on: a job."
///
/// Both sentences came from the same mistake, made twice. The scorer took the learner's goal as
/// their field, so "find a job" became the field "a job" - which every job advert ever written
/// mentions - and it also matched the goal text against the advert, where "job" hits again. The
/// two together were worth thirty-eight points on top of a base of forty-eight, so an advert
/// with nothing to do with international relations arrived in the nineties.
///
/// LearningIntentReader was written for exactly this distinction and says so in its own summary:
/// "a goal and a subject are not the same thing". The learning path had been taught it. The
/// opportunity scorer never was.
/// </remarks>
public class GoalIsNotAFieldTests
{
    // ── the classification the fix rests on ──────────────────────────────────

    /// <summary>An outcome is never a term to match an advert against.</summary>
    [Theory]
    [InlineData("find a job")]
    [InlineData("get a job")]
    [InlineData("new job")]
    [InlineData("I want to get hired")]
    [InlineData("get promoted")]
    [InlineData("earn more")]
    [InlineData("start a business")]
    [InlineData("lead a team")]
    public void AnOutcomeIsNotASubject(string goal)
    {
        Assert.False(LearningIntentReader.NamesASubject(goal));
    }

    /// <summary>And a subject still is one, or the fix would cost every real match.</summary>
    [Theory]
    [InlineData("International Relations")]
    [InlineData("Cybersecurity")]
    [InlineData("public health")]
    [InlineData("financial modelling")]
    public void ASubjectIsStillASubject(string goal)
    {
        Assert.True(LearningIntentReader.NamesASubject(goal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotASubject(string? goal)
    {
        Assert.False(LearningIntentReader.NamesASubject(goal));
    }

    // ── the field the scorer is allowed to use ───────────────────────────────

    /// <summary>
    /// The field comes from what they said they follow, never from what they want to happen.
    /// </summary>
    /// <remarks>
    /// The exact profile from the report: interested in International Relations, goal of finding
    /// a job. The field has to read as International Relations. If it reads as anything to do
    /// with a job, every job advert matches it.
    /// </remarks>
    [Fact]
    public void TheFieldOfSomebodyLookingForWorkIsStillTheirField()
    {
        var learner = new UserProfile
        {
            Persona = PersonaType.Graduate,
            PersonaChosenAt = DateTime.UtcNow,
            Interests = ["International Relations"],
            Goals = ["find a job"],
            PrimaryGoal = "find a job",
        };

        var intent = LearningIntentReader.Read(learner);

        Assert.Equal("International Relations", intent.Domain);
        Assert.NotEqual(LearningOutcome.LearnSubject, intent.Outcome);
    }

    /// <summary>
    /// The field never becomes the goal, whatever the goal says.
    /// </summary>
    /// <remarks>
    /// The scorer reads Domain and matches adverts against it. Any goal wording that leaks into
    /// that slot is the reported bug returning, so this asserts the property rather than one
    /// phrasing of it: whatever somebody writes as a goal, the field stays what they follow.
    /// </remarks>
    [Theory]
    [InlineData("find a job")]
    [InlineData("a job")]
    [InlineData("get hired somewhere good")]
    [InlineData("earn more money")]
    [InlineData("become a manager")]
    public void NoGoalWordingBecomesTheField(string goal)
    {
        var learner = new UserProfile
        {
            Persona = PersonaType.Graduate,
            PersonaChosenAt = DateTime.UtcNow,
            Interests = ["International Relations"],
            Goals = [goal],
            PrimaryGoal = goal,
        };

        Assert.Equal("International Relations", LearningIntentReader.Read(learner).Domain);
    }

    /// <summary>
    /// With nothing to go on, the field is a placeholder - and a placeholder is not a term.
    /// </summary>
    /// <remarks>
    /// This is the hole the backstop exists for. A profile with no interests, no role and no
    /// industry gets "your field", which reads correctly in a sentence and matches every advert
    /// equally well. The scorer has to recognise it and decline to match on it.
    /// </remarks>
    [Fact]
    public void AProfileWithNoFieldNamesNoField()
    {
        var learner = new UserProfile
        {
            Persona = PersonaType.Graduate,
            PersonaChosenAt = DateTime.UtcNow,
            Goals = ["find a job"],
            PrimaryGoal = "find a job",
        };

        Assert.Equal(LearningIntentReader.NoFieldNamed, LearningIntentReader.Read(learner).Domain);
    }

    // ── the sentence the reader was shown ────────────────────────────────────

    /// <summary>
    /// An advert in somebody else's field does not match their goal text.
    /// </summary>
    /// <remarks>
    /// OpportunityFit.Matching is the function that produced "It fits what you said you are
    /// working on: a job" against a .NET role. Filtering the goals to subjects before they reach
    /// it is what stops that sentence being written.
    /// </remarks>
    [Fact]
    public void AnAdvertDoesNotMatchAGoalThatIsAnOutcome()
    {
        const string advert = "lemon.io: senior .net full-stack developer. remote. a job for an "
                              + "experienced engineer who wants new job opportunities.";

        string[] goals = ["find a job", "International Relations"];
        var subjects = goals.Where(LearningIntentReader.NamesASubject);

        var matched = OpportunityFit.Matching(advert, subjects);

        Assert.Empty(matched);
    }

    /// <summary>And an advert in their field still does.</summary>
    [Fact]
    public void AnAdvertInTheirFieldStillMatches()
    {
        const string advert = "Policy analyst, international relations desk, Abuja. "
                              + "Research and briefing for the West Africa team.";

        string[] goals = ["find a job", "International Relations"];
        var subjects = goals.Where(LearningIntentReader.NamesASubject);

        Assert.Contains("International Relations", OpportunityFit.Matching(advert, subjects));
    }
}

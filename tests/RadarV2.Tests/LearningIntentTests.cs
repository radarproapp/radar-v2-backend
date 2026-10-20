using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The distinction that makes the whole learning path sensible: a goal can name a RESULT
/// ("get promoted") or a SUBJECT ("cybersecurity"), and the thing to learn is different in each
/// case. Confusing the two is what produced "Foundations of Get Promotion".
/// </summary>
public class LearningIntentTests
{
    [Theory]
    [InlineData("Get Promotion", LearningOutcome.Promotion)]
    [InlineData("I want to get promoted this year", LearningOutcome.Promotion)]
    [InlineData("Get a job in data analytics", LearningOutcome.NewJob)]
    [InlineData("Move into leadership", LearningOutcome.Leadership)]
    [InlineData("Start a business", LearningOutcome.StartBusiness)]
    [InlineData("Earn more", LearningOutcome.EarnMore)]
    public void Reads_the_outcome_from_the_goal(string goal, LearningOutcome expected)
    {
        var intent = LearningIntentReader.Read(goal, role: null, industry: null, interests: null);
        Assert.Equal(expected, intent.Outcome);
    }

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("Data analytics")]
    [InlineData("Learn Power BI")]
    public void Treats_a_field_as_a_subject_not_an_outcome(string goal)
    {
        var intent = LearningIntentReader.Read(goal, role: null, industry: null, interests: null);

        Assert.Equal(LearningOutcome.LearnSubject, intent.Outcome);
        Assert.False(intent.IsOutcomeDriven);
    }

    [Fact]
    public void Outcome_goals_take_their_domain_from_the_profile_not_the_goal()
    {
        // Someone chasing a promotion still needs to get better at their actual field. If the
        // domain came from the goal it would be "promotion", which nothing can teach.
        var intent = LearningIntentReader.Read(
            "Get Promotion", role: "SOC Analyst", industry: "Cybersecurity", interests: ["Cybersecurity"]);

        Assert.True(intent.IsOutcomeDriven);
        Assert.Equal("Cybersecurity", intent.Domain);
        Assert.DoesNotContain("promotion", intent.Domain, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outcome_subject_reads_as_a_phrase_not_a_pasted_goal()
    {
        var intent = LearningIntentReader.Read(
            "Get Promotion", role: "SOC Analyst", industry: null, interests: null);

        // "Getting promoted as a SOC Analyst", not "Get Promotion".
        Assert.Contains("SOC Analyst", intent.Subject);
        Assert.DoesNotContain("Get Promotion", intent.Subject);
    }

    [Fact]
    public void Handles_a_profile_with_almost_nothing_set()
    {
        var intent = LearningIntentReader.Read("", role: null, industry: null, interests: null);

        Assert.False(string.IsNullOrWhiteSpace(intent.Subject));
        Assert.False(string.IsNullOrWhiteSpace(intent.Domain));
    }
}

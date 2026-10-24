using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// No persona may be invisible to opportunity matching.
///
/// Graduate was. It appeared in neither scorer, so a graduate fell to the default and got no
/// persona matching at all — while <see cref="OpportunityType.GraduateProgramme"/>, the single
/// most relevant type there is for one, matched nobody. Two gaps that existed only because
/// nothing had been written for the persona between studying and working.
/// </summary>
public class PersonaCoverageTests
{
    private static UserProfile Profile(PersonaType persona) => new()
    {
        Id = "u1",
        Persona = persona,
        PrimaryGoal = "Software Engineering",
        Interests = ["Software Engineering"],
    };

    private static Opportunity Opp(OpportunityType type) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = "Software Engineering opening",
        Organisation = "Example",
        Type = type,
    };

    /// <summary>Scored higher for the right persona than for one it does not suit.</summary>
    private static int Score(PersonaType persona, OpportunityType type) =>
        InterestPersonalization.OpportunityScore(Opp(type), Profile(persona));

    [Theory]
    [InlineData(OpportunityType.GraduateProgramme)]
    [InlineData(OpportunityType.Internship)]
    public void AGraduateScoresHigherOnGraduateOpportunitiesThanAProfessionalDoes(OpportunityType type)
    {
        Assert.True(
            Score(PersonaType.Graduate, type) > Score(PersonaType.YoungProfessional, type),
            $"A graduate should out-score a professional on {type}.");
    }

    // Fellowship was in the theory above until it failed, correctly: a fellowship suits someone
    // leaving university and someone a few years into work equally, and scoring one above the
    // other would be inventing a preference neither stated.
    [Fact]
    public void AFellowshipSuitsAGraduateAndAProfessionalAlike()
    {
        Assert.Equal(
            Score(PersonaType.Graduate, OpportunityType.Fellowship),
            Score(PersonaType.YoungProfessional, OpportunityType.Fellowship));
    }

    // The type that named the persona and matched nobody.
    [Fact]
    public void GraduateProgrammesMatchSomebody()
    {
        var best = Enum.GetValues<PersonaType>()
            .Max(persona => Score(persona, OpportunityType.GraduateProgramme));

        var unmatched = Score(PersonaType.Entrepreneur, OpportunityType.GraduateProgramme);

        Assert.True(best > unmatched, "No persona is scored for a graduate programme.");
    }

    [Fact]
    public void AStudentStillOutScoresAGraduateOnAScholarship()
    {
        Assert.True(
            Score(PersonaType.Student, OpportunityType.Scholarship)
            > Score(PersonaType.Graduate, OpportunityType.Scholarship));
    }

    // The guard against this recurring: every persona Radar offers at signup must be scored for
    // something, or it silently gets the default and nobody notices for months.
    [Theory]
    [InlineData(PersonaType.Student)]
    [InlineData(PersonaType.Graduate)]
    [InlineData(PersonaType.YoungProfessional)]
    [InlineData(PersonaType.Entrepreneur)]
    [InlineData(PersonaType.Researcher)]
    public void EveryPersonaIsScoredForSomething(PersonaType persona)
    {
        var baseline = Score(persona, OpportunityType.Event);

        var best = Enum.GetValues<OpportunityType>()
            .Max(type => Score(persona, type));

        Assert.True(best > baseline, $"{persona} is scored the same for everything.");
    }
}

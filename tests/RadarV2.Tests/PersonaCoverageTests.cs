using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// No persona may be invisible to opportunity matching.
///
/// Graduate was. It appeared in no scorer at all, so a graduate fell to the default and got no
/// persona matching — while <see cref="OpportunityType.GraduateProgramme"/>, the single most
/// relevant type there is for one, matched nobody.
///
/// These tests used to run against a second scorer that no production code called, so for every
/// one of them the thing being asserted and the thing shipping were different objects. One of
/// them asserted a behaviour the live table does not have. A test pointed at dead code is worse
/// than no test: it reports a guarantee nobody has.
/// </summary>
public class PersonaCoverageTests
{
    /// <summary>What the live table says, which is what a reader's feed is ordered by.</summary>
    private static int Score(PersonaType persona, OpportunityType type) =>
        OpportunityFit.PointsFor(persona, type);

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

    // The same reasoning as the fellowship above, reached the other way round. This asserted
    // that a student out-scores a graduate on a scholarship, and the live table scores them
    // equally - correctly. A final-year student and somebody who graduated in June are both
    // exactly who a scholarship is for, and ranking one above the other invents a preference
    // neither of them stated. The test was passing against a scorer nothing called.
    [Fact]
    public void AScholarshipSuitsAStudentAndAGraduateAlike()
    {
        Assert.Equal(
            Score(PersonaType.Student, OpportunityType.Scholarship),
            Score(PersonaType.Graduate, OpportunityType.Scholarship));
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
        var best = Enum.GetValues<OpportunityType>()
            .Max(type => Score(persona, type));

        // Against the no-signal default rather than against a particular type. An event is worth
        // something to everybody now, so using one as the baseline would have made this pass for
        // a persona nothing else was written for.
        Assert.True(
            best > OpportunityFit.NoSignal,
            $"{persona} is scored the same for everything.");
    }
}

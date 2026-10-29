using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Which kinds of opportunity suit which kind of person.
/// </summary>
/// <remarks>
/// This was a switch expression inside a 160-line scoring method, and what it left out could not
/// be seen: Graduate appeared in it nowhere, so every graduate fell to the no-match default while
/// GraduateProgramme matched nobody at all.
///
/// A table can be read. It can also be checked, which is what this does.
/// </remarks>
public class OpportunityFitTests
{
    private static readonly PersonaType[] EveryPersona =
        Enum.GetValues<PersonaType>();

    /// <summary>
    /// A graduate is matched on the things a graduate applies for.
    /// </summary>
    [Fact]
    public void AGraduateIsMatchedOnGraduateOpenings()
    {
        foreach (var type in new[]
                 {
                     OpportunityType.GraduateProgramme,
                     OpportunityType.Internship,
                     OpportunityType.Fellowship,
                     OpportunityType.Job,
                 })
        {
            Assert.True(
                OpportunityFit.IsASignal(PersonaType.Graduate, type),
                $"A graduate got no signal from {type}.");
        }
    }

    /// <summary>
    /// Every persona has something written for it.
    /// </summary>
    /// <remarks>
    /// The specific failure: a persona nobody wrote a line for is not obviously broken from the
    /// outside. The feed still fills, the scores still differ, and the reason is that one of five
    /// kinds of person is being matched on nothing but the words in the advert.
    /// </remarks>
    [Fact]
    public void EveryPersonaHasAtLeastOneKindOfOpportunityWrittenForIt()
    {
        foreach (var persona in EveryPersona)
        {
            var matched = Enum.GetValues<OpportunityType>()
                .Count(type => OpportunityFit.PointsFor(persona, type) == OpportunityFit.Stage);

            Assert.True(matched > 0, $"Nothing is written for {persona}.");
        }
    }

    /// <summary>
    /// The things anybody can attend count for everybody.
    /// </summary>
    /// <remarks>
    /// Named in the spec: conferences, seminars, workshops, job fairs. They scored four points -
    /// the no-match default - for every persona at once, so a student looking for a job fair and
    /// a professional looking for the same job fair were both told it was a poor match.
    ///
    /// Where somebody is in a career changes which job they apply for. It does not change whether
    /// a workshop in their field is worth a Saturday.
    /// </remarks>
    [Theory]
    [InlineData(OpportunityType.Conference)]
    [InlineData(OpportunityType.Seminar)]
    [InlineData(OpportunityType.Workshop)]
    [InlineData(OpportunityType.JobFair)]
    public void AnyoneCanBeOfferedTheThingsAnyoneCanAttend(OpportunityType type)
    {
        foreach (var persona in EveryPersona)
        {
            Assert.True(
                OpportunityFit.IsASignal(persona, type),
                $"A {persona} got no signal from a {type}.");
        }
    }

    /// <summary>
    /// A seminar and a job fair exist as types at all.
    /// </summary>
    /// <remarks>
    /// Both are named in the spec and neither was in the enum, so a seminar was filed as a
    /// generic Event and a job fair had nowhere to go. Nothing could filter for either.
    /// </remarks>
    [Fact]
    public void TheTypesTheSpecNamesExist()
    {
        foreach (var named in new[]
                 {
                     "Job", "Internship", "Fellowship", "ResearchGrant", "Funding",
                     "GraduateProgramme", "Scholarship", "Conference", "Seminar", "Workshop",
                     "JobFair",
                 })
        {
            Assert.True(
                Enum.TryParse<OpportunityType>(named, out _),
                $"There is no opportunity type for {named}.");
        }
    }

    /// <summary>
    /// The types that were already stored keep the values they were stored with.
    /// </summary>
    /// <remarks>
    /// Persisted by value, so a type inserted in the middle of the list relabels every
    /// opportunity already in the database — a Job becomes an Internship and nobody notices
    /// until somebody reads their feed.
    /// </remarks>
    [Fact]
    public void TheExistingTypesKeepTheirStoredValues()
    {
        Assert.Equal(0, (int)OpportunityType.Job);
        Assert.Equal(1, (int)OpportunityType.Internship);
        Assert.Equal(14, (int)OpportunityType.Funding);
    }

    /// <summary>
    /// A type nobody wrote a rule for is not evidence against an opportunity.
    /// </summary>
    [Fact]
    public void AnUnwrittenTypeStillScoresSomething()
    {
        Assert.True(OpportunityFit.PointsFor(PersonaType.Student, OpportunityType.Funding) > 0);
    }
}

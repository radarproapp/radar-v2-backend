using RadarV2.Models;

namespace RadarV2.Tests.UseCases;

/// <summary>
/// The library itself, before anything is run through it.
/// </summary>
/// <remarks>
/// A corpus that quietly holds four hundred and ninety cases, or that never asks a student
/// anything, reports a clean night for the wrong reason.
/// </remarks>
public class UseCaseLibraryTests
{
    [Fact]
    public void ThereAreFiveHundredOfThem()
    {
        Assert.Equal(500, UseCaseLibrary.All.Count);
    }

    /// <summary>Every use case goes through every feature, which is what using Radar is.</summary>
    /// <remarks>
    /// A nurse matched correctly in opportunities and shown nursery school news in her feed has
    /// been failed, however well either half scores alone - and a suite asking one question per
    /// person could not see it.
    /// </remarks>
    [Fact]
    public void EveryUseCaseGoesThroughEveryFeature()
    {
        var features = Enum.GetValues<Feature>();

        foreach (var useCase in UseCaseLibrary.Batch(1, 5))
        {
            var covered = UseCaseRunner.Run(useCase).Select(o => o.Feature).ToHashSet();

            Assert.Equal(features.Length, covered.Count);
        }
    }

    /// <summary>All three kinds of user, and enough of each to mean something.</summary>
    [Theory]
    [InlineData(PersonaType.Student)]
    [InlineData(PersonaType.Graduate)]
    [InlineData(PersonaType.YoungProfessional)]
    public void EveryKindOfUserIsRepresented(PersonaType persona)
    {
        var mine = UseCaseLibrary.All.Count(c => c.Persona == persona);

        Assert.True(mine >= 100, $"only {mine} use cases for {persona}");
    }

    /// <summary>And every career is asked about by all three.</summary>
    [Fact]
    public void EveryCareerIsAskedAboutByAllThreeKindsOfUser()
    {
        var thin = CareerCorpus.All
            .Where(career => UseCaseLibrary.All
                .Where(c => c.Career.Name == career.Name)
                .Select(c => c.Persona)
                .Distinct()
                .Count() < 3)
            .Select(c => c.Name)
            .ToList();

        Assert.True(thin.Count == 0, $"not asked by all three: {string.Join(", ", thin)}");
    }

    /// <summary>
    /// Deliberately not a technology corpus.
    /// </summary>
    /// <remarks>
    /// The bug that prompted this was invisible to a software engineer and obvious to an
    /// International Relations student. A corpus that drifts back towards technology would stop
    /// being able to see it.
    /// </remarks>
    [Fact]
    public void MostOfTheCareersAreNotTechnology()
    {
        string[] technology =
        [
            "Software Engineering", "Data Analysis", "Cybersecurity",
            "UI and UX Design", "Product Management",
        ];

        var tech = CareerCorpus.All.Count(c => technology.Contains(c.Name));

        Assert.True(tech * 4 <= CareerCorpus.All.Count, $"{tech} of {CareerCorpus.All.Count} careers are technology");
    }

    /// <summary>Numbered, so a report can name one and somebody can find it.</summary>
    [Fact]
    public void EveryUseCaseHasItsOwnNumber()
    {
        Assert.Equal(UseCaseLibrary.All.Count, UseCaseLibrary.All.Select(c => c.Id).Distinct().Count());
    }

    /// <summary>Batches tile the whole set, with nothing dropped between them.</summary>
    [Fact]
    public void TheBatchesCoverEveryUseCaseExactlyOnce()
    {
        var seen = new List<int>();
        for (var batch = 1; batch <= 20; batch++)
        {
            seen.AddRange(UseCaseLibrary.Batch(batch).Select(c => c.Id));
        }

        Assert.Equal(500, seen.Count);
        Assert.Equal(500, seen.Distinct().Count());
    }
}

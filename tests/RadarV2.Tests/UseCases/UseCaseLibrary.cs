using RadarV2.Models;

namespace RadarV2.Tests.UseCases;

/// <summary>What a use case is asking Radar to get right.</summary>
public enum Asks
{
    /// <summary>An item in their field outranks one that is not.</summary>
    TheirFieldOutranksAnother,

    /// <summary>A posting in their field reads as theirs; one in another field does not.</summary>
    AnAdvertInTheirFieldIsTheirs,

    /// <summary>A headline that merely shares letters with their subject is not a match.</summary>
    LettersAreNotAMatch,

    /// <summary>A posting is filed as the kind of thing it says it is.</summary>
    APostingIsFiledCorrectly,

    /// <summary>A paper in their field comes before one that is not.</summary>
    TheirResearchComesFirst,

    /// <summary>Their field, not their goal, is what Radar matches on.</summary>
    TheirFieldIsNotTheirGoal,
}

/// <summary>
/// One concrete person, wanting one concrete thing, and what Radar owes them.
/// </summary>
public sealed record UseCase(int Id, Career Career, PersonaType Persona, Asks Asks)
{
    public string Title => $"#{Id} {Persona} in {Career.Name}: {Asks}";
}

/// <summary>
/// Five hundred use cases: fifty careers, ten each, across all three kinds of user.
/// </summary>
/// <remarks>
/// Expanded from the career corpus rather than typed out, so adding a career adds its ten and
/// the set cannot drift out of step with itself. Every one is a real shape of person - a nursing
/// graduate looking for work, a law student after a scholarship, a working accountant reading
/// the feed - and every one has an answer that is checkable without a model, an account, or a
/// penny of credit.
///
/// They run against the deterministic surfaces that actually decide what somebody sees: the
/// content scorer, the opportunity matcher, the subject vocabulary, the intent reader, the
/// research ranking and the filing audit. That is deliberate. The bug that prompted all of this
/// - an International Relations student shown three senior AI engineering roles at ninety per
/// cent - was in a scorer, not in a model, and a thousand model calls would not have found it
/// while one profile and one advert do.
/// </remarks>
public static class UseCaseLibrary
{
    /// <summary>
    /// The ten questions asked of every career, spread across the three kinds of user.
    /// </summary>
    /// <remarks>
    /// Students, graduates and professionals each get the questions that matter to them, and
    /// each career is asked all three - because a matcher that works for a graduate and fails
    /// for a student has failed for a third of the people who signed up.
    /// </remarks>
    private static readonly (PersonaType Persona, Asks Asks)[] PerCareer =
    [
        (PersonaType.Student, Asks.TheirFieldOutranksAnother),
        (PersonaType.Student, Asks.AnAdvertInTheirFieldIsTheirs),
        (PersonaType.Student, Asks.LettersAreNotAMatch),
        (PersonaType.Student, Asks.TheirFieldIsNotTheirGoal),

        (PersonaType.Graduate, Asks.TheirFieldOutranksAnother),
        (PersonaType.Graduate, Asks.AnAdvertInTheirFieldIsTheirs),
        (PersonaType.Graduate, Asks.APostingIsFiledCorrectly),

        (PersonaType.YoungProfessional, Asks.TheirFieldOutranksAnother),
        (PersonaType.YoungProfessional, Asks.AnAdvertInTheirFieldIsTheirs),
        (PersonaType.YoungProfessional, Asks.TheirResearchComesFirst),
    ];

    /// <summary>Every use case, numbered so a report can name one.</summary>
    public static readonly IReadOnlyList<UseCase> All = Build();

    private static List<UseCase> Build()
    {
        var cases = new List<UseCase>();
        var id = 1;

        foreach (var career in CareerCorpus.All)
        {
            foreach (var (persona, asks) in PerCareer)
            {
                cases.Add(new UseCase(id++, career, persona, asks));
            }
        }

        return cases;
    }

    /// <summary>One batch of them, so a run can be reported and fixed before the next.</summary>
    public static IReadOnlyList<UseCase> Batch(int number, int size = 25) =>
        [.. All.Skip((number - 1) * size).Take(size)];

    /// <summary>
    /// The person this use case is about.
    /// </summary>
    /// <remarks>
    /// Goals are the ones people actually write, and they are outcomes rather than subjects on
    /// purpose: "find a job" is what a graduate types, and it is the exact wording that was
    /// being read as a field and matching every job advert ever written.
    /// </remarks>
    public static UserProfile Learner(this UseCase useCase) => new()
    {
        Id = $"use-case-{useCase.Id}",
        Persona = useCase.Persona,
        PersonaChosenAt = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        Interests = [useCase.Career.Interest],
        Goals = [GoalFor(useCase.Persona)],
        PrimaryGoal = GoalFor(useCase.Persona),
    };

    private static string GoalFor(PersonaType persona) => persona switch
    {
        PersonaType.Student => "get a scholarship",
        PersonaType.Graduate => "find a job",
        _ => "advance my career",
    };
}

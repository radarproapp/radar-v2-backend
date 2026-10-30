using RadarV2.Models;

namespace RadarV2.Tests.UseCases;

/// <summary>Which part of Radar a result came from.</summary>
/// <remarks>
/// Every use case goes through all of them. A learner does not use one feature - they sign up,
/// read the feed, look at opportunities, build a path, search research - and a product that
/// matches a nurse correctly in opportunities and shows her nursery school news in the feed has
/// failed her, however well either half scores on its own.
/// </remarks>
public enum Feature
{
    /// <summary>The Intelligence feed: does their field outrank somebody else's?</summary>
    Feed,

    /// <summary>Category priorities: do they apply without overriding what the reader follows?</summary>
    FeedCategory,

    /// <summary>Why a card says it is in front of them, which has to be true.</summary>
    WhyShown,

    /// <summary>Opportunities: is an advert in their field theirs, and another's not?</summary>
    Opportunities,

    /// <summary>Whether the kinds of opening that suit their stage actually score for it.</summary>
    OpportunityStage,

    /// <summary>The subject vocabulary: letters in common are not a subject in common.</summary>
    Vocabulary,

    /// <summary>Radar Learn: the field is what they follow, never the outcome they typed.</summary>
    LearnIntent,

    /// <summary>Research Discovery: does their own field come first?</summary>
    Research,

    /// <summary>The nightly filing audit: is a posting classified at all?</summary>
    Filing,
}

/// <summary>
/// One person, with a career, a stage and something they are trying to do.
/// </summary>
/// <remarks>
/// A use case is a learner, not a question. Running one puts them through every feature in
/// <see cref="Feature"/> and reports what each gave them - because that is what using Radar is,
/// and because the failures that matter are the ones where one feature serves somebody well and
/// the next one does not.
/// </remarks>
public sealed record UseCase(int Id, Career Career, PersonaType Persona, string Goal)
{
    public string Who => $"#{Id} {Persona} in {Career.Name}, wanting to {Goal}";
}

/// <summary>
/// Five hundred people, each put through the whole product.
/// </summary>
public static class UseCaseLibrary
{
    /// <summary>
    /// Ten learners per career, spread across the three kinds of user.
    /// </summary>
    /// <remarks>
    /// The goals are the words people actually type, and they are outcomes rather than subjects
    /// on purpose: "find a job" is what a graduate writes, and it is the exact wording that was
    /// being read as a field and matching every job advert ever written.
    /// </remarks>
    private static readonly (PersonaType Persona, string Goal)[] PerCareer =
    [
        (PersonaType.Student, "get a scholarship"),
        (PersonaType.Student, "improve academically"),
        (PersonaType.Student, "find an internship"),

        (PersonaType.Graduate, "find a job"),
        (PersonaType.Graduate, "get onto a graduate trainee programme"),
        (PersonaType.Graduate, "fund a masters"),

        (PersonaType.YoungProfessional, "advance my career"),
        (PersonaType.YoungProfessional, "earn more"),
        (PersonaType.YoungProfessional, "move into leadership"),
        (PersonaType.YoungProfessional, "win a contract"),
    ];

    /// <summary>Every use case, numbered so a report can name one.</summary>
    public static readonly IReadOnlyList<UseCase> All = Build();

    private static List<UseCase> Build()
    {
        var cases = new List<UseCase>();
        var id = 1;

        foreach (var career in CareerCorpus.All)
        {
            foreach (var (persona, goal) in PerCareer)
            {
                cases.Add(new UseCase(id++, career, persona, goal));
            }
        }

        return cases;
    }

    /// <summary>One batch of them, so a run can be reported and fixed before the next.</summary>
    public static IReadOnlyList<UseCase> Batch(int number, int size = 25) =>
        [.. All.Skip((number - 1) * size).Take(size)];

    /// <summary>The person, as Radar would hold them after they personalised.</summary>
    public static UserProfile Learner(this UseCase useCase) => new()
    {
        Id = $"use-case-{useCase.Id}",
        Persona = useCase.Persona,
        PersonaChosenAt = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        Interests = [useCase.Career.Interest],
        Goals = [useCase.Goal],
        PrimaryGoal = useCase.Goal,
    };
}

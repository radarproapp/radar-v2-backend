using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.UseCases;

/// <summary>What happened when one use case was put to Radar.</summary>
/// <remarks>
/// <paramref name="Evidence"/> is the whole point of the record. A batch that reports forty
/// failures and cannot say what each one actually saw is a number, and a number is not a
/// finding - somebody still has to reproduce every one by hand before anything can be fixed.
/// </remarks>
public sealed record Outcome(UseCase UseCase, bool Passed, string Expected, string Actual)
{
    public string Report => Passed
        ? $"PASS  {UseCase.Title}"
        : $"FAIL  {UseCase.Title}\n        expected: {Expected}\n        actual:   {Actual}";
}

/// <summary>
/// Puts one use case to the parts of Radar that decide what a person sees.
/// </summary>
/// <remarks>
/// No model, no account, no credit. Every question here is settled by a deterministic service -
/// the content scorer, the opportunity matcher, the subject vocabulary, the intent reader, the
/// research ranking, the filing audit - which is where the failures of this kind have all
/// actually been.
/// </remarks>
public static class UseCaseRunner
{
    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    public static Outcome Run(UseCase useCase) => useCase.Asks switch
    {
        Asks.TheirFieldOutranksAnother => FieldOutranks(useCase),
        Asks.AnAdvertInTheirFieldIsTheirs => AdvertIsTheirs(useCase),
        Asks.LettersAreNotAMatch => LettersAreNotAMatch(useCase),
        Asks.APostingIsFiledCorrectly => FiledCorrectly(useCase),
        Asks.TheirResearchComesFirst => ResearchComesFirst(useCase),
        Asks.TheirFieldIsNotTheirGoal => FieldIsNotGoal(useCase),
        _ => new Outcome(useCase, false, "a question this runner knows", $"{useCase.Asks}, which it does not"),
    };

    public static IReadOnlyList<Outcome> RunAll(IEnumerable<UseCase> cases) => [.. cases.Select(Run)];

    // ── the checks ───────────────────────────────────────────────────────────

    /// <summary>Something in their field scores above something that is not.</summary>
    private static Outcome FieldOutranks(UseCase useCase)
    {
        var learner = useCase.Learner();

        var theirs = Item(useCase.Career.OnTopic, useCase.Career.Interest);
        var somebodyElses = Item(useCase.Career.OffTopic, "Software Engineering");

        var mine = InterestPersonalization.ContentScore(theirs, learner);
        var other = InterestPersonalization.ContentScore(somebodyElses, learner);

        return new Outcome(
            useCase,
            mine > other,
            $"\"{Short(useCase.Career.OnTopic)}\" above \"{Short(useCase.Career.OffTopic)}\"",
            $"their field scored {mine}, the other scored {other}");
    }

    /// <summary>An advert in their field reads as theirs; one in another field does not.</summary>
    private static Outcome AdvertIsTheirs(UseCase useCase)
    {
        var interest = useCase.Career.Interest;

        var matchesTheirs = DomainVocabulary.MentionsSubject(useCase.Career.OnTopic, interest);
        var matchesOthers = DomainVocabulary.MentionsSubject(useCase.Career.OffTopic, interest);

        return new Outcome(
            useCase,
            matchesTheirs && !matchesOthers,
            $"\"{interest}\" matches their own advert and not somebody else's",
            $"theirs: {(matchesTheirs ? "matched" : "MISSED")}, other field's: {(matchesOthers ? "WRONGLY MATCHED" : "not matched")}");
    }

    /// <summary>
    /// A headline that merely contains their subject's letters is not about them.
    /// </summary>
    /// <remarks>
    /// Every confusable in the corpus has a precedent here: "art" matched "start date", "mass
    /// communication" matched Massachusetts, "banking" matched bankruptcy, "foundations of
    /// Mathematics" matched the Heinrich Boell Foundation.
    /// </remarks>
    private static Outcome LettersAreNotAMatch(UseCase useCase)
    {
        var matched = DomainVocabulary.MentionsSubject(useCase.Career.Confusable, useCase.Career.Interest);

        return new Outcome(
            useCase,
            !matched,
            $"\"{useCase.Career.Interest}\" does not match \"{Short(useCase.Career.Confusable)}\"",
            matched ? "it matched" : "it did not match");
    }

    /// <summary>A posting is filed as the kind of thing its own title says it is.</summary>
    private static Outcome FiledCorrectly(UseCase useCase)
    {
        // Stored as a plain Job, which is the default a job board gives everything.
        var posting = new Opportunity
        {
            Id = $"use-case-{useCase.Id}",
            Title = useCase.Career.OnTopic,
            Description = string.Empty,
            Type = OpportunityType.Job,
        };

        var finding = FilingAudit.AuditType(posting);

        // Either it is genuinely a job, or the audit says what it really is. What must not
        // happen is the audit quietly confirming a type it never read.
        var sound = finding.Verdict is FilingAudit.Verdict.CorrectlyFiled or FilingAudit.Verdict.Misfiled;

        return new Outcome(
            useCase,
            sound,
            $"a verdict on \"{Short(useCase.Career.OnTopic)}\"",
            $"{finding.Verdict}: {finding.Evidence}");
    }

    /// <summary>A paper in their field comes before one that is not.</summary>
    private static Outcome ResearchComesFirst(UseCase useCase)
    {
        var learner = useCase.Learner();

        var theirs = Paper($"A study of {useCase.Career.Interest} practice in Nigeria", useCase.Career.Interest, citations: 12);
        var somebodyElses = Paper("A landmark result in particle physics", "Physics", citations: 40_000);

        var ordered = ResearchRanking.Ordered([somebodyElses, theirs], learner, Now);

        return new Outcome(
            useCase,
            ordered[0].Id == theirs.Id,
            $"the {useCase.Career.Interest} paper first",
            $"\"{Short(ordered[0].Title)}\" came first");
    }

    /// <summary>
    /// What Radar matches on is their field, never the outcome they typed as a goal.
    /// </summary>
    /// <remarks>
    /// The check the reported bug would have failed. A graduate whose goal is "find a job" had
    /// that read as their field, so "a job" matched every job advert in existence.
    /// </remarks>
    private static Outcome FieldIsNotGoal(UseCase useCase)
    {
        var learner = useCase.Learner();
        var domain = LearningIntentReader.Read(learner).Domain;

        return new Outcome(
            useCase,
            string.Equals(domain, useCase.Career.Interest, StringComparison.OrdinalIgnoreCase),
            $"the field reads as \"{useCase.Career.Interest}\"",
            $"it reads as \"{domain}\" (their goal was \"{learner.PrimaryGoal}\")");
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private static ContentItem Item(string title, string topic) => new()
    {
        Id = title,
        Title = title,
        Topic = topic,
        Type = ContentType.Article,
        PublishedAt = Now.AddDays(-2),
    };

    private static ContentItem Paper(string title, string topic, int citations) => new()
    {
        Id = title,
        Title = title,
        Topic = topic,
        Type = ContentType.ResearchPaper,
        PublishedAt = Now.AddDays(-200),
        CitationCount = citations,
        Journal = "A journal of record",
        Authors = ["A. Researcher"],
    };

    private static string Short(string text) => text.Length <= 48 ? text : text[..45].TrimEnd() + "...";
}

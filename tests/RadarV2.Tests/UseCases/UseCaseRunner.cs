using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.UseCases;

/// <summary>What one feature gave one person.</summary>
/// <remarks>
/// <paramref name="Actual"/> is the whole point. A batch reporting forty failures and no
/// evidence is a number, and a number is not a finding - somebody still has to reproduce every
/// one by hand before anything can be fixed.
/// </remarks>
public sealed record Outcome(UseCase UseCase, Feature Feature, bool Passed, string Expected, string Actual)
{
    public string Report => Passed
        ? $"PASS  {UseCase.Who} :: {Feature}"
        : $"FAIL  {UseCase.Who} :: {Feature}\n        expected: {Expected}\n        actual:   {Actual}";
}

/// <summary>
/// Puts one person through the whole of Radar.
/// </summary>
/// <remarks>
/// Every feature, for every use case, because that is what using the product is. A nurse
/// matched correctly in opportunities and shown nursery school news in her feed has been failed,
/// however well either half scores alone - and a suite that only ever asked one question per
/// person could not see it.
///
/// No model, no account, no credit. Each question is settled by a deterministic service, which
/// is where the failures of this kind have all actually been: the bug that prompted this - an
/// International Relations student shown three senior AI engineering roles at ninety per cent -
/// was in a scorer, and a thousand model calls would not have found what one profile and one
/// advert find immediately.
/// </remarks>
public static class UseCaseRunner
{
    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Every feature, for one person.</summary>
    public static IReadOnlyList<Outcome> Run(UseCase useCase) =>
    [
        Feed(useCase),
        FeedCategory(useCase),
        WhyShown(useCase),
        Opportunities(useCase),
        OpportunityStage(useCase),
        Vocabulary(useCase),
        LearnIntent(useCase),
        Research(useCase),
        Filing(useCase),
    ];

    public static IReadOnlyList<Outcome> RunAll(IEnumerable<UseCase> cases) => [.. cases.SelectMany(Run)];

    // ── Radar Intelligence ───────────────────────────────────────────────────

    /// <summary>Something in their field scores above something that is not.</summary>
    private static Outcome Feed(UseCase useCase)
    {
        var learner = useCase.Learner();

        var theirs = Item(useCase.Career.OnTopic, useCase.Career.Interest);
        var somebodyElses = Foil(useCase);

        var mine = InterestPersonalization.ContentScore(theirs, learner);
        var other = InterestPersonalization.ContentScore(somebodyElses, learner);

        return new Outcome(useCase, Feature.Feed, mine > other,
            $"\"{Short(useCase.Career.OnTopic)}\" above \"{Short(useCase.Career.OffTopic)}\"",
            $"their field scored {mine}, the other {other}");
    }

    /// <summary>
    /// Category priorities lift what their stage is for without outranking their field.
    /// </summary>
    /// <remarks>
    /// The spec is explicit that these are "starting priorities, not restrictions" and that
    /// "the user's actual interests and goals must influence what appears". A scholarship notice
    /// in somebody else's subject must not come before news in their own.
    /// </remarks>
    private static Outcome FeedCategory(UseCase useCase)
    {
        var learner = useCase.Learner();

        var theirs = Item(useCase.Career.OnTopic, useCase.Career.Interest);
        var stageBait = Item("Scholarship applications open for software engineering students", NotTheirs(useCase));

        var mine = InterestPersonalization.ContentScore(theirs, learner);
        var bait = InterestPersonalization.ContentScore(stageBait, learner);

        return new Outcome(useCase, Feature.FeedCategory, mine > bait,
            "their own field above a stage-flavoured item in another field",
            $"their field scored {mine}, the stage bait {bait}");
    }

    /// <summary>
    /// The reason on the card names something that actually matched.
    /// </summary>
    /// <remarks>
    /// The product's hardest rule. A card claiming relevance it did not compute is worse than
    /// one claiming none - "It is a job work, which is what you are learning" was that failure,
    /// in those words.
    /// </remarks>
    private static Outcome WhyShown(UseCase useCase)
    {
        var learner = useCase.Learner();
        var somebodyElses = Foil(useCase);

        var explained = InterestPersonalization.Explain(learner, somebodyElses);
        var why = RadarV2.Services.WhyShown.Build(somebodyElses, explained.MatchedSignals);

        // Nothing of theirs matched, so nothing of theirs may be named.
        var claimsTheirField = why.Contains(useCase.Career.Interest, StringComparison.OrdinalIgnoreCase);

        return new Outcome(useCase, Feature.WhyShown, !claimsTheirField,
            $"an off-topic card does not claim \"{useCase.Career.Interest}\"",
            why);
    }

    // ── Opportunities ────────────────────────────────────────────────────────

    /// <summary>An advert in their field reads as theirs; one in another field does not.</summary>
    private static Outcome Opportunities(UseCase useCase)
    {
        var interest = useCase.Career.Interest;

        var theirs = DomainVocabulary.MentionsSubject(useCase.Career.OnTopic, interest);
        var others = DomainVocabulary.MentionsSubject(useCase.Career.OffTopic, interest);

        return new Outcome(useCase, Feature.Opportunities, theirs && !others,
            $"\"{interest}\" matches their own advert and not another field's",
            $"theirs: {(theirs ? "matched" : "MISSED")}, other field's: {(others ? "WRONGLY MATCHED" : "not matched")}");
    }

    /// <summary>
    /// The kinds of opening their stage is for actually score for it.
    /// </summary>
    /// <remarks>
    /// Both gaps found here were invisible by inspection, because the no-match default is four
    /// points rather than an error: a Fellowship scored nothing for a Student and a Competition
    /// nothing for a Graduate, each named in the spec for that stage.
    /// </remarks>
    private static Outcome OpportunityStage(UseCase useCase)
    {
        var wanted = useCase.Persona switch
        {
            PersonaType.Student => OpportunityType.Scholarship,
            PersonaType.Graduate => OpportunityType.GraduateProgramme,
            _ => OpportunityType.Job,
        };

        var points = OpportunityFit.PointsFor(useCase.Persona, wanted);

        return new Outcome(useCase, Feature.OpportunityStage, points == OpportunityFit.Stage,
            $"a {wanted} scores for a {useCase.Persona}",
            $"it scored {points}, where a stage match is {OpportunityFit.Stage} and no signal is {OpportunityFit.NoSignal}");
    }

    /// <summary>
    /// A headline that merely contains their subject's letters is not about them.
    /// </summary>
    /// <remarks>
    /// Every confusable in the corpus has a precedent: "art" matched "start date", "mass
    /// communication" matched Massachusetts, "banking" matched bankruptcy, "foundations of
    /// Mathematics" matched the Heinrich Boell Foundation.
    /// </remarks>
    private static Outcome Vocabulary(UseCase useCase)
    {
        var matched = DomainVocabulary.MentionsSubject(useCase.Career.Confusable, useCase.Career.Interest);

        return new Outcome(useCase, Feature.Vocabulary, !matched,
            $"\"{useCase.Career.Interest}\" does not match \"{Short(useCase.Career.Confusable)}\"",
            matched ? "it matched" : "it did not match");
    }

    // ── Radar Learn ──────────────────────────────────────────────────────────

    /// <summary>
    /// What Radar matches on is their field, never the outcome they typed as a goal.
    /// </summary>
    /// <remarks>
    /// The check the reported bug would have failed: a graduate whose goal is "find a job" had
    /// that read as their field, so "a job" matched every job advert in existence.
    /// </remarks>
    private static Outcome LearnIntent(UseCase useCase)
    {
        var learner = useCase.Learner();
        var domain = LearningIntentReader.Read(learner).Domain;

        return new Outcome(useCase, Feature.LearnIntent,
            string.Equals(domain, useCase.Career.Interest, StringComparison.OrdinalIgnoreCase),
            $"the field reads as \"{useCase.Career.Interest}\"",
            $"it reads as \"{domain}\" (their goal was \"{useCase.Goal}\")");
    }

    /// <summary>A paper in their field comes before a much-cited one that is not.</summary>
    private static Outcome Research(UseCase useCase)
    {
        var learner = useCase.Learner();

        var theirs = Paper($"A study of {useCase.Career.Interest} practice in Nigeria", useCase.Career.Interest, 12);
        var famous = Paper("A landmark result in particle physics", "Physics", 40_000);

        var ordered = ResearchRanking.Ordered([famous, theirs], learner, Now);

        return new Outcome(useCase, Feature.Research, ordered[0].Id == theirs.Id,
            $"the {useCase.Career.Interest} paper first",
            $"\"{Short(ordered[0].Title)}\" came first");
    }

    // ── the nightly sweep ────────────────────────────────────────────────────

    /// <summary>A posting gets a verdict rather than a silent confirmation.</summary>
    private static Outcome Filing(UseCase useCase)
    {
        var posting = new Opportunity
        {
            Id = $"use-case-{useCase.Id}",
            Title = useCase.Career.OnTopic,
            Description = string.Empty,
            Type = OpportunityType.Job,
        };

        var finding = FilingAudit.AuditType(posting);
        var sound = finding.Verdict is FilingAudit.Verdict.CorrectlyFiled or FilingAudit.Verdict.Misfiled;

        return new Outcome(useCase, Feature.Filing, sound,
            $"a verdict on \"{Short(useCase.Career.OnTopic)}\"",
            $"{finding.Verdict}: {finding.Evidence}");
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Somebody else's card, and reliably somebody else's.</summary>
    /// <remarks>
    /// The foil's topic was hard-coded to "Software Engineering", which is fine for forty-nine
    /// careers and a lie for the fiftieth: the scorer reads Title and Topic together, so for a
    /// software engineering learner the card meant to be another field's was tagged with their
    /// own. All three feed checks failed for those ten people, and WhyShown truthfully printed
    /// "Matches your interest in Software Engineering" about a card titled "Clinical nurse
    /// specialist, intensive care" - the explanation was right and the fixture was lying to it.
    ///
    /// It was the only career failing four features, and none of it was a product defect.
    /// </remarks>
    private static ContentItem Foil(UseCase useCase) => Item(useCase.Career.OffTopic, NotTheirs(useCase));

    /// <summary>A topic that is never the learner's own.</summary>
    private static string NotTheirs(UseCase useCase) =>
        useCase.Career.Interest.Equals("Software Engineering", StringComparison.OrdinalIgnoreCase)
            ? "Nursing"
            : "Software Engineering";

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

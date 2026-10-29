using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// The gate every lesson resource passes before a learner sees it.
/// </summary>
/// <remarks>
/// Keyword matching decides what is worth asking about. This decides what is shown, and it is the
/// only thing that does: an item that has not been judged is not displayed.
///
/// Why a model and not a better score. The failure is about meaning, not spelling. "Understanding
/// Numbers" and "Anthropic IPO numbers surprise on the upside" genuinely share their most specific
/// word; no amount of tuning the matcher makes that pair look wrong, because on the evidence a
/// matcher has, it isn't. Knowing that one is arithmetic for a beginner and the other is a funding
/// round needs to know what the words are about.
///
/// Three rules it works under, from Radar's own:
///
/// <list type="bullet">
/// <item><b>Judged against what the learner asked for.</b> The lesson and the goal they typed —
/// never a persona, a degree or an inferred interest. Radar asks and does not infer.</item>
/// <item><b>Nothing unjudged is shown.</b> If the engine cannot answer, the lesson has no
/// readings and says so. An empty reading list is honest; a wrong one spends the learner's time
/// and their trust.</item>
/// <item><b>The reason is the model's, or there is none.</b> It returns why each item was kept,
/// and that is what gets stored. Nothing writes a justification on its behalf.</item>
/// </list>
/// </remarks>
public class LessonResourceJudge
{
    private readonly IAiEngine _ai;
    private readonly ILogger<LessonResourceJudge> _log;

    public LessonResourceJudge(IAiEngine ai, ILogger<LessonResourceJudge> log)
    {
        _ai = ai;
        _log = log;
    }

    /// <summary>The engine feature name, so its spend and failures are attributable.</summary>
    public const string Feature = "lesson-relevance";

    /// <summary>
    /// How many candidates are put to one call.
    /// </summary>
    /// <remarks>
    /// Enough that a lesson with a thin corpus still has something to choose from, small enough
    /// that the answer fits well inside the non-generation token budget. Judging is a short
    /// verdict per item, not prose.
    /// </remarks>
    public const int MaxCandidates = 16;

    /// <summary>
    /// One thing being considered for a lesson, and everything known about it.
    /// </summary>
    /// <remarks>
    /// <paramref name="Summary"/> is the publisher's own words for an article, and for a video it
    /// is the description YouTube returned. That description was being fetched, cached, and then
    /// not passed on, so the summariser was asked to describe a video from its title and channel
    /// alone - which is why it produced a sentence restating the title, or nothing at all.
    /// </remarks>
    public sealed record Candidate(string Id, string Title, string Source, string? Summary);

    /// <summary>
    /// What the judge decided, and what it has to say about what it kept.
    /// </summary>
    /// <remarks>
    /// Covers and Why are two different questions and a learner needs both: what is in this
    /// thing, and what it gives them for the lesson they are on. They were one 15-word field
    /// written as an audit note for a filter, and that fragment ended up on the card as the whole
    /// explanation.
    /// </remarks>
    public sealed record Verdict(string Id, bool Keep, string Covers, string Why);

    /// <summary>
    /// What came back. <see cref="Items"/> is null when the field was absent.
    /// </summary>
    /// <remarks>
    /// Nullable rather than initialised to an empty list, and that is the whole defence against
    /// the worst failure this gate can have.
    ///
    /// The engine never returns null. When the provider is unreachable, out of credit, rate
    /// limited or past its deadline, it falls through to a deterministic brain, and for a feature
    /// that brain has no template for it synthesises "{}". Deserialised into a class whose list
    /// was initialised to empty, that is indistinguishable from the model having read every
    /// candidate and kept none.
    ///
    /// So a spend cap or a rotated key read as "the corpus has nothing for this lesson" — which
    /// was then written down as a final answer, on every lesson, permanently, wiping the very
    /// resources this gate exists to replace. An absent field now reads as no answer at all.
    /// </remarks>
    private sealed class JudgementResponse
    {
        public List<JudgedItem>? Items { get; set; }
    }

    public sealed class JudgedItem
    {
        /// <summary>
        /// Echoed back so a verdict cannot be attached to the wrong item.
        /// </summary>
        /// <remarks>
        /// Position in the request is not an identifier. A model that returns eleven verdicts for
        /// twelve candidates, or reorders them, would otherwise shift every decision onto its
        /// neighbour — and the result would look entirely normal.
        /// </remarks>
        public string? Id { get; set; }

        public bool Keep { get; set; }

        /// <summary>What is in it. Two or three sentences.</summary>
        public string? Covers { get; set; }

        /// <summary>What it gives this learner for this lesson. One or two sentences.</summary>
        public string? Why { get; set; }
    }

    /// <summary>
    /// Judges candidates for one lesson. Returns null when no judgement could be made.
    /// </summary>
    /// <remarks>
    /// Null and empty mean different things and the caller has to tell them apart. Empty is "the
    /// engine looked and none of these belong here", which is a finished answer. Null is "the
    /// engine could not be reached", which is not an answer at all, and the lesson has to say so
    /// rather than imply the corpus holds nothing.
    /// </remarks>
    public async Task<IReadOnlyList<Verdict>?> JudgeAsync(
        string lessonTitle,
        string lessonBody,
        string learningGoal,
        IReadOnlyList<string> otherLessons,
        IReadOnlyList<Candidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0) return [];

        var considered = candidates.Take(MaxCandidates).ToList();

        try
        {
            var result = await _ai.GenerateJsonAsync<JudgementResponse>(
                Feature,
                SystemPrompt,
                BuildPrompt(lessonTitle, lessonBody, learningGoal, otherLessons, considered),
                ct);

            // Null, or a body with no items field at all: not an answer. See JudgementResponse.
            if (result?.Items is null) return null;

            return Reconcile(considered, result.Items);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not judge resources for a lesson.");
            return null;
        }
    }

    /// <summary>
    /// Matches verdicts back to candidates by id, and treats anything unmatched as not kept.
    /// </summary>
    /// <remarks>
    /// A candidate the model said nothing about has not been approved, and silence is not consent
    /// when the question is what to put in front of somebody. Dropping it costs one reading;
    /// keeping it is the bug this whole gate exists to stop.
    /// </remarks>
    public static IReadOnlyList<Verdict> Reconcile(
        IReadOnlyList<Candidate> candidates, IReadOnlyList<JudgedItem> judged)
    {
        var byId = new Dictionary<string, JudgedItem>(StringComparer.Ordinal);
        foreach (var item in judged)
        {
            if (!string.IsNullOrWhiteSpace(item.Id)) byId[item.Id!] = item;
        }

        var verdicts = new List<Verdict>();

        foreach (var candidate in candidates)
        {
            if (!byId.TryGetValue(candidate.Id, out var decision) || !decision.Keep) continue;

            // Kept without one or the other is still kept, and nothing is invented to fill the
            // gap. A card missing a line is honest; a line Radar wrote on a judgement's behalf is
            // the small fabrication the rest of the product refuses to make.
            verdicts.Add(new Verdict(
                candidate.Id,
                true,
                decision.Covers?.Trim() ?? string.Empty,
                decision.Why?.Trim() ?? string.Empty));
        }

        return verdicts;
    }

    private const string SystemPrompt =
        "You decide whether a resource belongs in a specific lesson of someone's learning path, " +
        "and you explain the ones that do. You are strict about what belongs: the question is not " +
        "whether the resource is interesting or whether it shares words with the lesson title, " +
        "but whether working through it would teach the learner what this lesson is about. News " +
        "about a company, a market or an event is almost never a lesson resource, even when it " +
        "uses the subject's vocabulary. When you explain one, write for the learner rather than " +
        "about the decision, and never state a detail the material in front of you does not " +
        "support. Return only valid JSON matching the requested schema.";

    /// <summary>
    /// What the rest of the path is about, as evidence of which subject this is.
    /// </summary>
    /// <remarks>
    /// A learner building a path in Architecture was offered resources on layered architecture —
    /// the software kind. Nothing was malfunctioning: a lesson titled "Introduction to
    /// Architecture", judged on its own, is genuinely ambiguous, and on the evidence available a
    /// post about software layers was a defensible match.
    ///
    /// The evidence that resolves it was already on the same page and was not being passed.
    /// Beside "Site analysis" and "Building regulations", the subject is not in doubt. One lesson
    /// does not say which field it belongs to; a path does.
    ///
    /// This does not fix an ambiguous goal. A path whose every lesson is generic stays ambiguous,
    /// and the honest answer there is to ask the learner rather than to guess better.
    /// </remarks>
    private static string PathContext(IReadOnlyList<string> otherLessons)
    {
        var named = otherLessons
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(8)
            .ToList();

        return named.Count == 0
            ? "(this is the only lesson on the path)"
            : string.Join("; ", named.Select(Shorten));
    }

    private static string BuildPrompt(
        string lessonTitle,
        string lessonBody,
        string learningGoal,
        IReadOnlyList<string> otherLessons,
        IReadOnlyList<Candidate> candidates)
    {
        // Every field flattened, not just the summary. Titles and sources arrive from ingested
        // RSS, so a newline in one would forge further candidate lines inside a prompt whose only
        // job is to be a strict filter — and it would be a feed publisher, not a learner, doing it.
        var lines = candidates.Select(c =>
            $"- id: {c.Id}\n  title: {Shorten(c.Title)}\n  source: {Shorten(c.Source)}\n  summary: {Shorten(c.Summary)}");

        return $$"""
            The learner is working towards: {{Shorten(Blank(learningGoal, "(not stated)"))}}

            The lesson is: {{Shorten(lessonTitle)}}
            What the lesson covers: {{Shorten(Blank(lessonBody, "(no description)"))}}

            The other lessons on this path: {{PathContext(otherLessons)}}
            Use those to settle which field this is. A word can belong to more than one subject,
            and the path says which one the learner means.

            Candidate resources:
            {{string.Join("\n", lines)}}

            For each candidate, decide whether it teaches what this lesson covers.

            Keep it only if a learner studying this lesson would be better off for having read or
            watched it. Drop it if it merely mentions the same words, if it reports an event rather
            than explaining a subject, or if it is about a different field that shares vocabulary.

            If the summary describes something other than the title, drop it: the item is not
            trustworthy enough to put in a lesson.

            It is correct to drop every candidate. Do not keep a weak one to avoid an empty list.

            For each one you keep, write two things the learner reads before deciding to spend
            time on it.

            "covers": two or three sentences on what is actually in it. Work only from the title,
            source and summary given above. Where that is thin, say what it indicates rather than
            inventing specifics, and if it is too thin to describe at all, return an empty string
            rather than a sentence that restates the title.

            "why": one or two sentences on what it gives this learner for THIS lesson, on the way
            to this goal. Name the part of the lesson it serves. Not a ranking, not a verdict on
            quality, and never "this is a great resource".

            Respond ONLY with valid JSON:
            {
              "items": [
                {
                  "id": "the id exactly as given",
                  "keep": true,
                  "covers": "What is in it, in two or three sentences.",
                  "why": "What it gives this learner for this lesson."
                }
              ]
            }
            """;
    }

    private static string Blank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(none)";

        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }
}

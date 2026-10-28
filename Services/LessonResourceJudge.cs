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

    /// <summary>One thing being considered for a lesson.</summary>
    public sealed record Candidate(string Id, string Title, string Source, string? Summary);

    /// <summary>What the judge decided about it.</summary>
    public sealed record Verdict(string Id, bool Keep, string Reason);

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

        public string? Reason { get; set; }
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
        IReadOnlyList<Candidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0) return [];

        var considered = candidates.Take(MaxCandidates).ToList();

        try
        {
            var result = await _ai.GenerateJsonAsync<JudgementResponse>(
                Feature, SystemPrompt, BuildPrompt(lessonTitle, lessonBody, learningGoal, considered), ct);

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

            // Kept without a reason is still kept, but nothing is invented to fill the line. The
            // card shows the summary alone rather than a sentence Radar made up about why.
            verdicts.Add(new Verdict(candidate.Id, true, decision.Reason?.Trim() ?? string.Empty));
        }

        return verdicts;
    }

    private const string SystemPrompt =
        "You decide whether a resource belongs in a specific lesson of someone's learning path. " +
        "You are strict. The question is not whether the resource is interesting or whether it " +
        "shares words with the lesson title, but whether working through it would teach the " +
        "learner what this lesson is about. News about a company, a market or an event is almost " +
        "never a lesson resource, even when it uses the subject's vocabulary. Return only valid " +
        "JSON matching the requested schema.";

    private static string BuildPrompt(
        string lessonTitle, string lessonBody, string learningGoal, IReadOnlyList<Candidate> candidates)
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

            Candidate resources:
            {{string.Join("\n", lines)}}

            For each candidate, decide whether it teaches what this lesson covers.

            Keep it only if a learner studying this lesson would be better off for having read or
            watched it. Drop it if it merely mentions the same words, if it reports an event rather
            than explaining a subject, or if it is about a different field that shares vocabulary.

            If the summary describes something other than the title, drop it: the item is not
            trustworthy enough to put in a lesson.

            It is correct to drop every candidate. Do not keep a weak one to avoid an empty list.

            Respond ONLY with valid JSON:
            {
              "items": [
                { "id": "the id exactly as given", "keep": true, "reason": "max 15 words, what the learner gets from it" }
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

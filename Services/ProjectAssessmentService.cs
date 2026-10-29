using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Judging an attempt at a project against the standards for its stage.
/// </summary>
/// <remarks>
/// A project used to be accepted by being marked complete — a button, with nothing behind it. So
/// "I built this" and "this is good enough" were the same claim, made by the same person, and a
/// portfolio of accepted projects meant only that somebody had pressed a button several times.
///
/// Three rules it works under, all of them shared with the document assessor next door:
///
/// <list type="bullet">
/// <item><b>The rubric drives the loop.</b> Criteria come from <see cref="ProjectStandards"/> and
/// the model answers them; a criterion it skips is reported unjudged rather than dropped, so the
/// learner sees the whole standard and which parts of it were actually applied.</item>
/// <item><b>The score is arithmetic.</b> The model is never asked for a number — see
/// <see cref="ProjectScore"/>.</item>
/// <item><b>What was read is stated.</b> For an image, a video or a link, the artefact was never
/// seen; what gets judged is the learner's own account, and the assessment says so.</item>
/// </list>
/// </remarks>
public class ProjectAssessmentService
{
    private readonly IAiEngine _ai;
    private readonly ILogger<ProjectAssessmentService> _log;

    public ProjectAssessmentService(IAiEngine ai, ILogger<ProjectAssessmentService> log)
    {
        _ai = ai;
        _log = log;
    }

    public const string Feature = "project-assessment";

    private sealed class JudgementResponse
    {
        /// <summary>
        /// Null when the field was absent, which is how a provider failure is told apart from a
        /// genuine answer with nothing in it.
        /// </summary>
        /// <remarks>
        /// The engine never returns null: a failed call falls through to a deterministic brain
        /// that synthesises "{}" for a feature it has no template for. Initialised to an empty
        /// list, that is indistinguishable from a model that judged nothing — which once meant
        /// every provider outage was recorded as a finding about the work.
        /// </remarks>
        public List<JudgedCriterion>? Criteria { get; set; }
    }

    private sealed class JudgedCriterion
    {
        public string? Name { get; set; }
        public string? Verdict { get; set; }
        public string? Evidence { get; set; }
        public string? Fix { get; set; }
    }

    /// <summary>
    /// Assesses one submission and returns the mark.
    /// </summary>
    public async Task<ProjectAssessment> AssessAsync(
        StudioProject project,
        ProjectSubmission submission,
        string stage,
        CancellationToken ct = default)
    {
        var rubric = ProjectStandards.For(stage, submission.Kind);
        var (body, judgedFrom) = WhatCanBeRead(submission);

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ProjectAssessment
            {
                Criteria = rubric,
                JudgedFrom = judgedFrom,

                // Nothing to read is not a bad project. It is a submission with no substance in
                // it yet, and saying which avoids marking somebody down for a missing file.
                NotAssessedReason =
                    "There was nothing to read in this submission. Attach the work, or describe "
                    + "what you built, and submit again.",
            };
        }

        JudgementResponse? judged = null;

        try
        {
            judged = await _ai.GenerateJsonAsync<JudgementResponse>(
                Feature, SystemPrompt, BuildPrompt(project, submission, stage, rubric, body), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A project assessment could not be made.");
        }

        if (judged?.Criteria is null)
        {
            return new ProjectAssessment
            {
                // The rubric still goes back. Knowing what the work will be judged against is
                // worth something even when the judgement did not happen.
                Criteria = rubric,
                JudgedFrom = judgedFrom,
                NotAssessedReason =
                    "Radar could not assess this just now. Nothing is wrong with your work — "
                    + "submit it again shortly.",
            };
        }

        foreach (var criterion in rubric)
        {
            var match = Matching(judged.Criteria, criterion.Name);

            criterion.Verdict = NormaliseVerdict(match?.Verdict);
            criterion.Evidence = Blank(match?.Evidence) ?? "Not assessed.";
            criterion.Fix = criterion.Verdict == "Met" ? null : Blank(match?.Fix);
        }

        // A rubric that came back half answered is not an assessment.
        //
        // Every criterion the model skipped, or renamed past recognition, became "Unjudged" and
        // was then scored as earning nothing - which read as a judgement about the work. Two ways
        // that went wrong. An answer of {"criteria": []} marked good work 1 out of 10 and told it
        // the criteria were "mostly unmet", with nothing saying the fault was Radar's. And four
        // answered of seven could still clear the bar, so a project was accepted with three of
        // its standards never looked at.
        //
        // So an incomplete answer is a failure to assess, said in those words, and the learner
        // submits again. The rubric still goes back with the lines that were answered marked, so
        // it is visible which parts were reached rather than a flat "try again".
        var unjudged = rubric.Count(c => c.Verdict is "Unjudged");

        if (unjudged > 0)
        {
            _log.LogWarning(
                "A project assessment answered {Judged} of {Total} criteria; not recording a mark.",
                rubric.Count - unjudged, rubric.Count);

            return new ProjectAssessment
            {
                Criteria = rubric,
                JudgedFrom = judgedFrom,
                NotAssessedReason =
                    $"Radar reached {rubric.Count - unjudged} of the {rubric.Count} standards for "
                    + "this stage and could not judge the rest, so there is no mark. Nothing is "
                    + "wrong with your work — submit it again shortly.",
            };
        }

        var score = ProjectScore.Of(rubric);

        return new ProjectAssessment
        {
            Criteria = rubric,
            Score = score,
            IsAccepted = ProjectScore.IsAccepted(score),
            Verdict = ProjectScore.Verdict(score),
            JudgedFrom = judgedFrom,
        };
    }

    /// <summary>
    /// What there is to judge, and what it is.
    /// </summary>
    /// <remarks>
    /// Returned together so the two can never disagree. The alternative — deciding the text in
    /// one place and describing it in another — is how a score on a description comes to be
    /// labelled as a score on the work.
    /// </remarks>
    private static (string Body, string JudgedFrom) WhatCanBeRead(ProjectSubmission submission)
    {
        if (!string.IsNullOrWhiteSpace(submission.ExtractedText))
        {
            var withNotes = string.IsNullOrWhiteSpace(submission.Notes)
                ? submission.ExtractedText
                : $"{submission.ExtractedText}\n\nWhat the learner says about it:\n{submission.Notes}";

            return (withNotes, $"Your {submission.Kind.ToString().ToLowerInvariant()}, as submitted.");
        }

        var what = submission.Kind switch
        {
            SubmissionKind.Image => "Radar cannot see images, so this is based on your description, not the image.",
            SubmissionKind.Video => "Radar cannot watch videos, so this is based on your description, not the video.",
            SubmissionKind.Link => "Radar does not open links yet, so this is based on your description, not the page.",
            _ => "Radar could not read the file, so this is based on your description.",
        };

        return (submission.Notes, what);
    }

    private const string SystemPrompt =
        "You assess a learner's project against a fixed rubric, one criterion at a time. Answer " +
        "only the criteria you are given, by name. For each, give a verdict, the evidence in the " +
        "work that led to it, and - when it is not met - the one specific change that would move " +
        "it. Evidence means something quoted or pointed at in what you were given, never an " +
        "adjective. Do not give a score, a grade or an overall opinion: the mark is worked out " +
        "from your verdicts.\n\n" +
        MetMeans +
        "\n\nThe stage decides which criteria apply, which has already been done for you. It does " +
        "not lower what any of them require: a beginner's project is held to the beginner " +
        "criteria in full, not to a softened version of them. Return only valid JSON matching " +
        "the requested schema.";

    /// <summary>
    /// What the three verdicts cost, stated once and shared.
    /// </summary>
    /// <remarks>
    /// Written after a two-line file reading "2 + 2 = 4" was assessed on production, scored nine
    /// out of ten and accepted. Four criteria came back Met. Nothing was broken: the arithmetic
    /// was right and the evidence quoted the work. The bar was simply never defined, so "does
    /// what it says" was satisfied by a document that said almost nothing.
    ///
    /// An assessment that accepts anything teaches a learner that the mark is decoration, and the
    /// first time they rely on it with real work is the time it costs them something.
    /// </remarks>
    internal const string MetMeans =
        "Met means the criterion is fully and clearly satisfied by what is in front of you, and " +
        "you can quote the part that satisfies it. Partly means it was attempted, or is present " +
        "but thin. NotMet means it is absent or inadequate.\n\n" +
        "Hold the line. Where you are uncertain between two verdicts, give the lower one - a " +
        "learner can argue a verdict up with evidence, and nobody can argue one down. Work that " +
        "is trivially short, that does the minimum, or that states a result without the working " +
        "behind it is not Met on any criterion about substance, however correct it is. Being " +
        "right is not the same as being enough.\n\n" +
        "You are not here to encourage. The encouragement is in telling somebody exactly what " +
        "would make their work good, which requires first saying that it is not yet.";

    private static string BuildPrompt(
        StudioProject project,
        ProjectSubmission submission,
        string stage,
        IReadOnlyList<AssessmentCriterion> rubric,
        string body)
    {
        var criteria = string.Join("\n", rubric.Select(c => $"- {c.Name}: {c.Asks}"));

        return $$"""
            A learner at the {{Blank(stage) ?? "beginner"}} stage has submitted a project.

            The project: {{project.Title}}
            What it was meant to produce: {{Blank(project.Description) ?? "(not stated)"}}

            Judge it against these criteria, and only these:
            {{criteria}}

            What was submitted:
            {{Truncate(body, 12000)}}

            Respond ONLY with valid JSON:
            {
              "criteria": [
                {
                  "name": "the criterion name exactly as given",
                  "verdict": "Met | Partly | NotMet",
                  "evidence": "what in the submission led to this, quoted or pointed at",
                  "fix": "the one change that would move this to Met, or omit when already met"
                }
              ]
            }
            """;
    }

    /// <summary>
    /// The model's answer for one rubric line, found by name.
    /// </summary>
    /// <remarks>
    /// Matched on the words rather than the exact string. The prompt asks for the name exactly as
    /// given and models mostly comply, but a trailing full stop, a parenthetical gloss or
    /// different capitalisation all used to count as no answer at all — and an unanswered
    /// criterion is now a refusal to mark, so near-misses are worth catching rather than
    /// punishing somebody for.
    ///
    /// Containment is accepted only when exactly one candidate contains the name or is contained
    /// by it. Two plausible matches means guessing which standard was judged, and attributing a
    /// verdict to the wrong criterion is worse than admitting the line went unanswered.
    /// </remarks>
    private static JudgedCriterion? Matching(List<JudgedCriterion> judged, string name)
    {
        var wanted = Words(name);

        var exact = judged.FirstOrDefault(c => Words(c.Name) == wanted);
        if (exact is not null) return exact;

        var near = judged
            .Where(c => Words(c.Name) is { Length: > 3 } other
                && (other.Contains(wanted) || wanted.Contains(other)))
            .ToList();

        return near.Count == 1 ? near[0] : null;
    }

    /// <summary>A name reduced to its letters and digits, for comparing two of them.</summary>
    private static string Words(string? name) =>
        new(( name ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string NormaliseVerdict(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "met" => "Met",
        "partly" or "partial" => "Partly",
        "notmet" or "not met" => "NotMet",
        _ => "Unjudged",
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n\n[truncated]";
}

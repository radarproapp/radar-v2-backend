using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Assesses a document somebody wrote, against criteria for what it is and who they are.
/// </summary>
/// <remarks>
/// Two model calls, deliberately separate. The first asks only what kind of document this is; the
/// second judges it against the rubric for that kind. Asking for both at once lets the model pick
/// the criteria it then marks against, which means nobody knows what the judgement was against
/// and the criteria drift between two runs on the same essay.
///
/// The existing mentor work review does neither: the caller supplies the document type from a
/// dropdown, the persona is never considered, and it returns a score out of 100 against no stated
/// rubric. A CV submitted with the default selected gets told its paragraphs are underdeveloped.
/// </remarks>
public class DocumentAssessmentService
{
    private readonly IAiEngine _ai;
    private readonly RadarDatabase _db;
    private readonly ILogger<DocumentAssessmentService> _log;

    public DocumentAssessmentService(IAiEngine ai, RadarDatabase db, ILogger<DocumentAssessmentService> log)
    {
        _ai = ai;
        _db = db;
        _log = log;
    }

    /// <summary>
    /// How much of the document the model sees.
    /// </summary>
    /// <remarks>
    /// Enough for a CV or an essay in full, and the opening of anything longer. Truncation is
    /// told to the model rather than hidden, so it does not criticise a conclusion it was not
    /// shown.
    /// </remarks>
    private const int MaxCharacters = 18_000;

    private sealed class KindResponse
    {
        public string? Kind { get; set; }
        public string? Evidence { get; set; }
    }

    private sealed class JudgementResponse
    {
        public List<CriterionJudgement> Criteria { get; set; } = [];
        public List<string> Strengths { get; set; } = [];
        public string? MostValuableChange { get; set; }
    }

    private sealed class CriterionJudgement
    {
        public string? Name { get; set; }
        public string? Verdict { get; set; }
        public string? Evidence { get; set; }
        public string? Fix { get; set; }
    }

    public async Task<DocumentAssessment> AssessAsync(
        UserProfile profile, string fileName, string text, CancellationToken ct = default)
    {
        var assessment = new DocumentAssessment
        {
            UserId = profile.Id,
            FileName = fileName,
            AssessedAs = profile.Persona,
        };

        var truncated = text.Length > MaxCharacters;
        var body = truncated ? text[..MaxCharacters] : text;

        var kind = await DetectKindAsync(body, truncated, ct);
        assessment.Kind = kind.Kind;
        assessment.KindEvidence = kind.Evidence;

        if (kind.Kind == DocumentKind.Unknown)
        {
            // Nothing is judged against a guessed rubric. Picking a default here is how a CV gets
            // marked as an essay, which is useless and reads as not having been looked at.
            assessment.WasAssessed = false;
            assessment.NotAssessedReason =
                "Radar could not tell what kind of document this is, so it has not been assessed. "
                + "An essay, report, CV, cover letter, personal statement, research proposal or business plan can be.";
            return await StoreAsync(assessment, ct);
        }

        var rubric = AssessmentRubric.For(kind.Kind, profile.Persona);
        var judged = await JudgeAsync(body, truncated, kind.Kind, profile, rubric, ct);

        if (judged is null)
        {
            assessment.WasAssessed = false;
            assessment.NotAssessedReason =
                "Radar identified the document but could not complete the assessment. Nothing here is a stand-in for it.";

            // The rubric still goes back. Knowing what it would have been judged against is worth
            // something even when the judgement failed.
            assessment.Criteria = rubric;
            return await StoreAsync(assessment, ct);
        }

        assessment.WasAssessed = true;
        assessment.Strengths = judged.Strengths.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        assessment.MostValuableChange = Blank(judged.MostValuableChange);

        // Driven by the authored rubric, not by what came back. A criterion the model skipped is
        // reported as unjudged rather than dropped, so the reader sees the whole rubric and knows
        // which parts were actually assessed.
        foreach (var criterion in rubric)
        {
            var match = judged.Criteria
                .FirstOrDefault(c => string.Equals(c.Name, criterion.Name, StringComparison.OrdinalIgnoreCase));

            criterion.Verdict = NormaliseVerdict(match?.Verdict);
            criterion.Evidence = Blank(match?.Evidence) ?? "Not assessed.";
            criterion.Fix = criterion.Verdict == "Met" ? null : Blank(match?.Fix);
        }

        assessment.Criteria = rubric;
        return await StoreAsync(assessment, ct);
    }

    private async Task<(DocumentKind Kind, string Evidence)> DetectKindAsync(
        string body, bool truncated, CancellationToken ct)
    {
        var prompt = $$"""
            Identify what kind of document this is. Do not assess it.

            Allowed kinds: Essay, Report, Cv, CoverLetter, ResearchProposal, PersonalStatement,
            BusinessPlan, Unknown.

            Answer Unknown unless the text clearly is one of them. A fragment, a set of notes, a
            syllabus, a textbook chapter or someone else's published work is Unknown — this is for
            assessing work the reader wrote.

            {{(truncated ? "You are seeing only the opening of a longer document." : "")}}

            ---
            {{body}}
            ---

            Return JSON: {"kind": "Cv", "evidence": "Opens with contact details followed by dated roles in reverse order."}
            The evidence must quote or describe what in the text identifies it, in one sentence.
            """;

        var response = await _ai.GenerateJsonAsync<KindResponse>(
            "document-kind",
            "You identify document types. You are precise and you answer Unknown rather than guessing.",
            prompt,
            ct);

        if (response?.Kind is null || !Enum.TryParse<DocumentKind>(response.Kind, true, out var kind))
        {
            _log.LogInformation("Document kind not identified for an assessment request.");
            return (DocumentKind.Unknown, string.Empty);
        }

        return (kind, Blank(response.Evidence) ?? string.Empty);
    }

    private async Task<JudgementResponse?> JudgeAsync(
        string body,
        bool truncated,
        DocumentKind kind,
        UserProfile profile,
        List<AssessmentCriterion> rubric,
        CancellationToken ct)
    {
        var criteriaList = string.Join("\n", rubric.Select(c => $"- {c.Name}: {c.Asks}"));

        // The goal is included because it changes what "good" means for a CV or a statement, and
        // it is something the user typed rather than something inferred about them.
        var goal = string.Join(", ", (profile.Goals ?? []).Where(g => !string.IsNullOrWhiteSpace(g)));
        var context = string.IsNullOrWhiteSpace(goal)
            ? $"The writer is a {profile.Persona}."
            : $"The writer is a {profile.Persona} whose stated goals are: {goal}.";

        var prompt = $$"""
            Assess this {{kind}} against the criteria below, and only those criteria.

            {{context}}

            Criteria:
            {{criteriaList}}

            {{(truncated ? "You are seeing only the opening of a longer document. Do not comment on parts you cannot see." : "")}}

            ---
            {{body}}
            ---

            Rules:
            - Verdict is exactly one of "Met", "Partly", "NotMet".
            - Evidence must quote or locate something in the document. Do not use adjectives alone.
            - Fix must be a specific change to this document, not general advice. Omit it when Met.
            - Strengths must be specific to this document. Return an empty list rather than filler.
            - mostValuableChange is the single change that would improve it most.
            - Do not invent a score or a grade.

            Return JSON:
            {
              "criteria": [
                {"name": "A position", "verdict": "Partly", "evidence": "The introduction lists three views but does not say which the essay defends.", "fix": "End the introduction with the claim the essay argues for."}
              ],
              "strengths": ["Paragraph four explains why the 2019 data undercuts the earlier finding."],
              "mostValuableChange": "State your position in the introduction, then order the body around defending it."
            }
            """;

        return await _ai.GenerateJsonAsync<JudgementResponse>(
            "document-assessment",
            "You assess written work against stated criteria. You quote evidence and you never invent a score.",
            prompt,
            ct);
    }

    /// <summary>Anything that is not one of the three verdicts is treated as not judged.</summary>
    private static string NormaliseVerdict(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "met" => "Met",
        "partly" or "partial" => "Partly",
        "notmet" or "not met" => "NotMet",
        _ => "Unjudged",
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<DocumentAssessment> StoreAsync(DocumentAssessment assessment, CancellationToken ct)
    {
        try
        {
            await _db.DocumentAssessments.InsertOneAsync(assessment, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // The assessment is still returned. Failing to file it is not a reason to withhold it
            // from the person who just uploaded their CV.
            _log.LogWarning(ex, "Could not store a document assessment.");
        }

        return assessment;
    }
}

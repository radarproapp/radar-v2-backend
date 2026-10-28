namespace RadarV2.Models;

/// <summary>
/// What kind of document somebody submitted for assessment.
/// </summary>
/// <remarks>
/// Detected from the text, never from the filename or a dropdown. A CV assessed as an essay gets
/// told its paragraphs are too short, which is useless and also slightly insulting.
/// </remarks>
public enum DocumentKind
{
    /// <summary>Radar could not tell. Nothing is assessed, and it says so.</summary>
    Unknown = 0,
    Essay,
    Report,
    Cv,
    CoverLetter,
    ResearchProposal,
    PersonalStatement,
    BusinessPlan,

    /// <summary>
    /// A post, caption, thread or content calendar written to be published on a social platform.
    /// </summary>
    /// <remarks>
    /// Added because somebody uploaded one and Radar said it could not work out what the document
    /// was. It was right that it could not — nothing in the list was close — but the list was the
    /// problem. Content writing is ordinary paid work for the students, graduates and
    /// professionals Radar is for, and a tool that assesses a cover letter but not a caption is
    /// making a judgement about which of their work counts.
    /// </remarks>
    SocialMediaContent,
}

/// <summary>
/// One thing the document was judged against, and how it did.
/// </summary>
/// <remarks>
/// Criteria are named and shown to the reader. The existing work review returns a score out of
/// 100 — "76", "78" — against no stated rubric, which is a number nobody can check or argue
/// with. A judgement against a criterion the reader can see is a judgement they can disagree
/// with, which is the difference between feedback and a verdict.
/// </remarks>
public class AssessmentCriterion
{
    public string Name { get; set; } = string.Empty;

    /// <summary>What this criterion asks of the document, in a sentence.</summary>
    public string Asks { get; set; } = string.Empty;

    /// <summary>Met, Partly, or NotMet. Deliberately three, not a percentage.</summary>
    public string Verdict { get; set; } = "Partly";

    /// <summary>What in the document led to that verdict. Quotes or locations, not adjectives.</summary>
    public string Evidence { get; set; } = string.Empty;

    /// <summary>The specific change that would move this to Met. Empty when already met.</summary>
    public string? Fix { get; set; }
}

public class DocumentAssessment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// False when Radar could not actually assess the document.
    /// </summary>
    /// <remarks>
    /// The same rule the work review learned the hard way: a parse failure there once produced a
    /// fabricated review, verified identical for a security incident report and a banana bread
    /// recipe, score included. Misleading feedback on work somebody is trying to improve is worse
    /// than no feedback, so this says nothing rather than something.
    /// </remarks>
    public bool WasAssessed { get; set; }

    /// <summary>Why not, when <see cref="WasAssessed"/> is false.</summary>
    public string? NotAssessedReason { get; set; }

    public DocumentKind Kind { get; set; }

    /// <summary>
    /// What in the text identified the kind, so a wrong guess is arguable rather than mysterious.
    /// </summary>
    public string KindEvidence { get; set; } = string.Empty;

    /// <summary>
    /// True when the reader told Radar what the document is, rather than Radar working it out.
    /// </summary>
    /// <remarks>
    /// Detection is not reliable enough to be the only route. Measured on staging: the same CV
    /// came back as Cv in full and as Unknown when shortened by a third. Being told "Radar could
    /// not tell what kind of document this is" about your own CV is honest and useless, so the
    /// reader can say what it is — which is the same rule the rest of the product runs on, asking
    /// rather than inferring.
    ///
    /// Recorded because it changes how much the result is worth: a declared kind means the
    /// criteria are certainly the right ones, and a detected kind means Radar thought so.
    /// </remarks>
    public bool KindWasDeclared { get; set; }

    /// <summary>
    /// The persona the criteria were shaped for. A graduate's CV and a professional's CV are
    /// judged against different things, and the reader should know which was applied.
    /// </summary>
    public PersonaType AssessedAs { get; set; }

    public List<AssessmentCriterion> Criteria { get; set; } = [];

    /// <summary>What the document does well. Specific, or omitted.</summary>
    public List<string> Strengths { get; set; } = [];

    /// <summary>
    /// The single most valuable change. One, not a list — a reader given eight equal priorities
    /// has been given none.
    /// </summary>
    public string? MostValuableChange { get; set; }

    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// How many criteria were met, for a summary line. Null when nothing was assessed.
    /// </summary>
    public string? Summary =>
        !WasAssessed || Criteria.Count == 0
            ? null
            : $"{Criteria.Count(c => c.Verdict == "Met")} of {Criteria.Count} criteria met";
}

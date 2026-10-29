namespace RadarV2.Models;

/// <summary>
/// What Radar made of one attempt at a project.
/// </summary>
/// <remarks>
/// A project is not accepted because it was handed in. It is judged against written standards for
/// the stage, scored out of ten from those judgements, and accepted at six — and everything
/// needed to argue with the mark is here beside it.
/// </remarks>
public class ProjectAssessment
{
    /// <summary>
    /// The rubric, with a verdict and evidence against each line.
    /// </summary>
    /// <remarks>
    /// The whole rubric, including anything the model did not judge. A criterion reported as
    /// unjudged tells the learner which parts were actually assessed; dropping it would quietly
    /// shorten the standard it was measured against.
    /// </remarks>
    public List<AssessmentCriterion> Criteria { get; set; } = [];

    /// <summary>
    /// One to ten, or null when nothing could be assessed.
    /// </summary>
    /// <remarks>
    /// Derived from the verdicts rather than asked for. Null is not zero: it means no judgement
    /// was made, which a mark of one would misreport as the work being bad.
    /// </remarks>
    public int? Score { get; set; }

    public bool IsAccepted { get; set; }

    /// <summary>What the mark means, pointing at the next action rather than at the learner.</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>
    /// What Radar actually read in order to judge this.
    /// </summary>
    /// <remarks>
    /// Said plainly, because it differs by submission: the work itself for a document or source
    /// file, and the learner's own written account for an image, a video or a link. A score on a
    /// description that reads as a score on the artefact is the fabrication this exists to
    /// prevent.
    /// </remarks>
    public string JudgedFrom { get; set; } = string.Empty;

    /// <summary>
    /// Why there is no assessment, when there is none.
    /// </summary>
    /// <remarks>
    /// Empty when one was made. A failed judgement is a fault and has to read like one rather
    /// than as a verdict on the work — the same distinction the feed and the lesson resources
    /// both had to learn.
    /// </remarks>
    public string? NotAssessedReason { get; set; }

    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;
}

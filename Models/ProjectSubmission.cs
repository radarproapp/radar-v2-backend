namespace RadarV2.Models;

/// <summary>
/// What a learner hands in.
/// </summary>
/// <remarks>
/// Five kinds, because what people build is not all one shape: a script, a design, a recording, a
/// written report, or something already live on the web.
/// </remarks>
public enum SubmissionKind
{
    /// <summary>Source files, or a repository.</summary>
    Code = 0,

    Image,
    Video,

    /// <summary>A report, an essay, a plan — anything Radar can read as text.</summary>
    Document,

    /// <summary>Something already published: a site, a repo, a deployed thing.</summary>
    Link,
}

/// <summary>
/// One attempt at a project.
/// </summary>
/// <remarks>
/// Kept as a list on the project rather than a single field, because a project that scores under
/// the bar is meant to be revised and handed in again — and the earlier attempts are the evidence
/// that it was. Overwriting them would erase the only record of somebody improving.
/// </remarks>
public class ProjectSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public SubmissionKind Kind { get; set; }

    /// <summary>
    /// Where it lives, for a link or anything Radar stored rather than read.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>The file as handed in, when there was one.</summary>
    public string? FileName { get; set; }

    /// <summary>
    /// What Radar could actually read of it.
    /// </summary>
    /// <remarks>
    /// Text for a document or source file. Empty for an image or a video, which Radar cannot read
    /// at all — and that emptiness is the honest record of it, not a gap to be filled in later by
    /// something that guessed from the filename.
    /// </remarks>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// What the learner says they built, in their own words.
    /// </summary>
    /// <remarks>
    /// Asked for always, and the only thing Radar has to go on for an image or a video. A
    /// submission assessed on this rather than on the artefact has to say so, which is why the
    /// two are stored separately instead of being run together into one blob of context.
    /// </remarks>
    public string Notes { get; set; } = string.Empty;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

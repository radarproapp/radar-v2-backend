namespace RadarV2.Models;

public enum LessonResourceKind
{
    Video = 0,
    Article = 1,
}

/// <summary>
/// Where the summary came from, which decides how much it is worth.
/// </summary>
/// <remarks>
/// Stated rather than implied. A summary written from a forty-minute transcript and one written
/// from a two-line YouTube description read identically on screen, and only one of them is a
/// summary of the thing you are about to watch. The feed already labels this on media detail
/// pages; a lesson that asks somebody to watch five videos has more reason to.
/// </remarks>
public enum SummarySource
{
    /// <summary>From the full transcript. Needs a transcription key, which is not set.</summary>
    Transcript = 0,

    /// <summary>From the title, channel and description YouTube returned.</summary>
    Description = 1,

    /// <summary>The publisher's own summary, already written when the item was ingested.</summary>
    Publisher = 2,
}

/// <summary>
/// One thing to watch or read for a lesson, with a summary of what is in it.
/// </summary>
/// <remarks>
/// Lean on purpose. <see cref="LearningResource"/> carries tutorial steps, practice exercises and
/// quiz topics because it represents a stage of a path; this represents one item on a watch list,
/// and the extra fields would all be empty.
/// </remarks>
public class LessonResource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public LessonResourceKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>The embeddable player, for videos that have one.</summary>
    public string? EmbedUrl { get; set; }

    public string? ThumbnailUrl { get; set; }

    /// <summary>The channel, or the publication. Named so the reader can judge it.</summary>
    public string Source { get; set; } = string.Empty;

    public string? Duration { get; set; }

    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// What is actually in it, in a few sentences.
    /// </summary>
    /// <remarks>
    /// Empty rather than invented when it could not be written. Five videos with three real
    /// summaries and two blanks is more use than five with two guesses in the middle.
    /// </remarks>
    public string Summary { get; set; } = string.Empty;

    public SummarySource SummarySource { get; set; } = SummarySource.Description;

    /// <summary>
    /// The id of the ingested content item, for articles that came from Radar's own corpus. Lets
    /// the client link into the brief rather than straight out to the publisher.
    /// </summary>
    public string? ContentItemId { get; set; }
}

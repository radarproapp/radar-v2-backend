namespace RadarV2.Models;

/// <summary>
/// A project as a stranger sees it.
/// </summary>
/// <remarks>
/// An allow-list, deliberately, rather than the stored document with the private parts removed.
///
/// The public endpoint returned the whole <see cref="StudioProject"/> to anybody with the id and
/// no sign-in: every submission, the full extracted text of the uploaded work, every assessment,
/// every score, and every criterion with its evidence quoting the work. Including the attempts
/// that were refused. Somebody who shared a link to a finished project was also publishing the
/// three tries it took and what an assessor said was wrong with each.
///
/// Section 16 asks that uploaded documents be protected and restricted to the authorised user,
/// and the Privacy Policy makes the same commitment. Neither survives a showcase link.
///
/// Written as its own type so the default is to disclose nothing. Removing fields from the
/// stored document would mean every field added later is public until somebody remembers it
/// should not be — and nobody ever remembers.
/// </remarks>
public class PublicProject
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>What the project set out to produce.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether it was accepted.
    /// </summary>
    /// <remarks>
    /// The mark itself is not here. That it met the standard is the useful public fact; what it
    /// scored, and which criteria it scraped through on, is between the learner and their
    /// assessment.
    /// </remarks>
    public bool IsAccepted { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>What a stranger is allowed to see of a project.</summary>
    public static PublicProject From(StudioProject project) => new()
    {
        Id = project.Id,
        Title = project.Title,
        Description = project.Description,
        IsAccepted = project.IsCompleted,
        CompletedAt = project.CompletedAt,
    };
}

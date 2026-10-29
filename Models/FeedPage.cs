namespace RadarV2.Models;

/// <summary>
/// A page of Radar Intelligence: what happened, and what you can apply for.
/// </summary>
/// <remarks>
/// Radar Intelligence is defined as three things — developments in the subjects somebody follows,
/// material that serves their goals and challenges, and opportunities they can actually act on.
/// The first two are content items and arrive in <see cref="PagedResult{T}.Items"/>. The third
/// could not: an opportunity carries a deadline, an organisation and a list of requirements, and
/// flattening it into a content item would throw all three away — the deadline most of all, which
/// is the only part of an opportunity that expires.
///
/// So it rides alongside rather than inside, and this type exists rather than a field on
/// <see cref="PagedResult{T}"/> because a page of items is a general shape used elsewhere and
/// opportunities are not part of what a page is.
///
/// Only on the first page. They are a standing set rather than a stream, and repeating them under
/// every scroll would be the same twelve things four times over.
/// </remarks>
public class FeedPage : PagedResult<object>
{
    /// <summary>
    /// Things this reader can apply for, matched to their goals, interests and challenges.
    /// </summary>
    /// <remarks>
    /// Empty on later pages, and empty when nothing is open — never padded. An opportunity shown
    /// to fill a row is worse than a gap, because a reader who applies for it has spent an
    /// afternoon on it.
    /// </remarks>
    public List<Opportunity> Opportunities { get; set; } = [];

    /// <summary>
    /// What the opportunities were matched against, in the reader's own words.
    /// </summary>
    /// <remarks>
    /// Shown so the match can be disagreed with. It used to default to "Cybersecurity", which
    /// told a brand new reader that Radar had matched against a focus they had never chosen.
    /// </remarks>
    public string OpportunityFocus { get; set; } = string.Empty;
}

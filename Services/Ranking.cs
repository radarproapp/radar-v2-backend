using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// The order things are shown in, in one place.
/// </summary>
/// <remarks>
/// Both feeds sorted on a single integer key with <c>List.Sort</c>, which is an unstable
/// introsort. Scores are whole numbers - a match percent is 0 to 99 and a content score is a small
/// integer - so across a few hundred candidates most items tie with several others, and the order
/// within a tie was whatever the sort happened to produce that time.
///
/// That is not only cosmetic. Paging re-runs the whole pipeline per request, so page 2 is sorted
/// separately from page 1: when tied items land in a different order the second time, an
/// opportunity can appear on both pages or on neither. A reader paging through a feed was
/// silently not being shown everything in it.
///
/// So every comparison here ends on the id. Not because the id means anything, but because it
/// makes the order total: given the same items and the same scores, the order is the same every
/// time, and a refresh stops reshuffling the screen.
///
/// Worth stating precisely, because the first version of this comment claimed more. A total order
/// fixes ties and nothing else. Two larger sources of page drift sit upstream and are untouched:
/// the match score is recomputed per request and an AI refinement step can succeed on one request
/// and fail on the next, moving items between bands wholesale; and the candidate window is the
/// newest five hundred, so ingestion between two requests pushes rows off the bottom. Paging is
/// deterministic here and still not stable end to end - that needs the ranking computed once and
/// held, which is its own piece of work.
/// </remarks>
public static class Ranking
{
    /// <summary>
    /// Opportunities, best match first.
    /// </summary>
    /// <remarks>
    /// Relevance leads, because that is what Radar claims to do: a strong match closing in two
    /// months is still a better thing to show than a weak one closing tomorrow, and promoting the
    /// weak one because it is urgent is how a feed fills with things nobody wants and a deadline.
    ///
    /// Urgency breaks ties instead. Among opportunities Radar rates equally, the one closing
    /// soonest is the one worth seeing first - a missed deadline is the one failure that cannot be
    /// recovered later. An opportunity with no stated deadline sorts after those that have one,
    /// which falls out of DaysUntilDeadline reporting int.MaxValue for them.
    /// </remarks>
    public static List<Opportunity> ByRelevance(IEnumerable<Opportunity> opportunities) =>
    [
        .. opportunities
            .OrderByDescending(o => o.MatchScorePercent)

            // Closed last, and this clause has to come first among the tiebreaks. DaysUntilDeadline
            // floors at zero for a deadline that has passed, so a closed opportunity reports the
            // smallest possible value and sorted to the very top of its score band - the feed is
            // served with a day's grace, so this was reachable on every request rather than being
            // a theoretical edge.
            //
            // Opportunity.DaysUntilDeadline says so in its own remarks: "Callers deciding what to
            // show must ask whether it has closed first." This did not ask.
            .ThenBy(o => o.HasClosed)

            .ThenBy(o => o.DaysUntilDeadline)
            .ThenByDescending(o => o.DiscoveredAt)
            .ThenBy(o => o.Id, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Content, most relevant first, scoring each item exactly once.
    /// </summary>
    /// <remarks>
    /// The score was being computed inside the comparator, so it ran once per comparison rather
    /// than once per item - for five hundred items that is roughly nine times the work, and the
    /// scorer builds a full explanation each call rather than reading a number off the item.
    ///
    /// Scoring up front also makes the comparison honest. A comparator that recomputes a value is
    /// only valid while that value cannot change between calls, and nothing guaranteed that; a
    /// scorer that is not perfectly consistent makes a sort throw rather than merely misorder.
    /// </remarks>
    public static List<ContentItem> ByRelevance(
        IEnumerable<ContentItem> items,
        Func<ContentItem, int> score)
    {
        var scored = items.Select(item => (Item: item, Score: score(item)));

        return
        [
            .. scored
                .OrderByDescending(entry => entry.Score)
                .ThenByDescending(entry => entry.Item.PublishedAt)
                .ThenBy(entry => entry.Item.Id, StringComparer.Ordinal)
                .Select(entry => entry.Item),
        ];
    }
}

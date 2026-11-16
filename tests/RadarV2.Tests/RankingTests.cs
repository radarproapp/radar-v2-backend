using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The order a reader sees things in, and the fact that it does not change under them.
/// </summary>
/// <remarks>
/// Both feeds sorted on one integer key with List.Sort, which is unstable. Scores are whole
/// numbers over a small range, so ties are the common case rather than the edge, and the order
/// within a tie was whatever that run produced. Paging re-runs the pipeline per request, so page 2
/// was sorted separately from page 1 - which is how an item ends up on both pages, or on neither.
/// </remarks>
public class RankingTests
{
    private static Opportunity Opp(string id, int score, int? daysToDeadline = null, DateTime? discovered = null) =>
        new()
        {
            Id = id,
            MatchScorePercent = score,
            Deadline = daysToDeadline is null ? null : DateTime.UtcNow.AddDays(daysToDeadline.Value),
            DiscoveredAt = discovered ?? new DateTime(2026, 1, 1),
        };

    // ── opportunities ────────────────────────────────────────────────────────

    [Fact]
    public void TheBestMatchComesFirst()
    {
        var ranked = Ranking.ByRelevance([Opp("a", 40), Opp("b", 90), Opp("c", 65)]);

        Assert.Equal(["b", "c", "a"], ranked.Select(o => o.Id));
    }

    /// <summary>
    /// Relevance leads and urgency breaks ties, not the other way round.
    /// </summary>
    /// <remarks>
    /// Promoting a weak match because it closes soon is how a feed fills with things nobody wants
    /// and a deadline. A strong match two months out is still the better thing to show.
    /// </remarks>
    [Fact]
    public void UrgencyBreaksTiesButDoesNotBeatRelevance()
    {
        var ranked = Ranking.ByRelevance([
            Opp("weak-but-urgent", 50, daysToDeadline: 1),
            Opp("strong-but-distant", 90, daysToDeadline: 60),
        ]);

        Assert.Equal(["strong-but-distant", "weak-but-urgent"], ranked.Select(o => o.Id));

        var tied = Ranking.ByRelevance([
            Opp("closes-later", 70, daysToDeadline: 30),
            Opp("closes-sooner", 70, daysToDeadline: 3),
        ]);

        Assert.Equal(["closes-sooner", "closes-later"], tied.Select(o => o.Id));
    }

    /// <summary>
    /// A closed opportunity never outranks a live one it ties with.
    /// </summary>
    /// <remarks>
    /// The defect this file failed to catch the first time, because every fixture in it had a
    /// future deadline or none. DaysUntilDeadline floors at zero for a deadline that has passed,
    /// so a closed opportunity reported the smallest possible value and sorted to the very top of
    /// its score band - above everything still open.
    ///
    /// Not an edge case: the feed deliberately keeps a day's grace after a deadline, so a
    /// bursary that shut six hours ago is in the candidate set and was being rendered first.
    /// Opportunity.DaysUntilDeadline warns about exactly this in its own remarks.
    /// </remarks>
    [Fact]
    public void AClosedOpportunityDoesNotOutrankALiveOneAtTheSameScore()
    {
        var ranked = Ranking.ByRelevance([
            Opp("closed-yesterday", 70, daysToDeadline: -1),
            Opp("closes-in-three-days", 70, daysToDeadline: 3),
            Opp("closes-in-a-month", 70, daysToDeadline: 30),
        ]);

        Assert.Equal(["closes-in-three-days", "closes-in-a-month", "closed-yesterday"],
            ranked.Select(o => o.Id));
    }

    /// <summary>Being closed loses a tie; it does not override the score.</summary>
    /// <remarks>
    /// A strong closed match still outranks a weak open one, because relevance leads. Burying
    /// closed items entirely is a filtering decision and belongs where the grace window is
    /// decided, not in the comparison.
    /// </remarks>
    [Fact]
    public void BeingClosedBreaksATieRatherThanBeatingTheScore()
    {
        var ranked = Ranking.ByRelevance([
            Opp("weak-and-open", 40, daysToDeadline: 5),
            Opp("strong-but-closed", 90, daysToDeadline: -1),
        ]);

        Assert.Equal(["strong-but-closed", "weak-and-open"], ranked.Select(o => o.Id));
    }

    /// <summary>An opportunity that states no deadline sorts after ones that do.</summary>
    [Fact]
    public void AnUndatedOpportunitySortsAfterEquallyMatchedDatedOnes()
    {
        var ranked = Ranking.ByRelevance([
            Opp("no-deadline", 70),
            Opp("has-deadline", 70, daysToDeadline: 45),
        ]);

        Assert.Equal(["has-deadline", "no-deadline"], ranked.Select(o => o.Id));
    }

    /// <summary>
    /// The same inputs give the same page, every time.
    /// </summary>
    /// <remarks>
    /// The defect this file exists for. Twenty opportunities on one score is not contrived: match
    /// percents are integers, so a few hundred candidates tie in groups this size routinely.
    /// Shuffling the input stands in for the arbitrary order an unstable sort produces.
    /// </remarks>
    [Fact]
    public void ATiedFieldIsOrderedTheSameWayEveryTime()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => Opp($"id-{i:D2}", 70, daysToDeadline: 30))
            .ToList();

        var fromOneOrder = Ranking.ByRelevance(items).Select(o => o.Id).ToList();
        var fromAnother = Ranking.ByRelevance(Enumerable.Reverse(items)).Select(o => o.Id).ToList();
        var fromAThird = Ranking.ByRelevance(items.OrderBy(o => o.Id[^1])).Select(o => o.Id).ToList();

        Assert.Equal(fromOneOrder, fromAnother);
        Assert.Equal(fromOneOrder, fromAThird);
    }

    /// <summary>
    /// Paging a tied field shows everything exactly once.
    /// </summary>
    /// <remarks>
    /// The consequence that actually reached people. Each request re-sorts, so with an unstable
    /// order page 1 and page 2 disagree about which items they contain - and the reader is never
    /// told, because a short page looks exactly like a feed that has run out.
    /// </remarks>
    [Fact]
    public void PagingATiedFieldLosesNothingAndRepeatsNothing()
    {
        var items = Enumerable.Range(0, 25)
            .Select(i => Opp($"id-{i:D2}", 70, daysToDeadline: 30))
            .ToList();

        const int pageSize = 10;
        var seen = new List<string>();

        for (var page = 0; page < 3; page++)
        {
            // Re-ranked from scratch per page, exactly as a second HTTP request would.
            seen.AddRange(Ranking.ByRelevance(items)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(o => o.Id));
        }

        Assert.Equal(25, seen.Count);
        Assert.Equal(25, seen.Distinct().Count());
        Assert.Equal(items.Select(o => o.Id).OrderBy(id => id), seen.OrderBy(id => id));
    }

    [Fact]
    public void RankingAnEmptyFeedIsNotAnError()
    {
        Assert.Empty(Ranking.ByRelevance(Array.Empty<Opportunity>()));
        Assert.Empty(Ranking.ByRelevance(Array.Empty<ContentItem>(), _ => 0));
    }

    // ── content ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Each item is scored once, not once per comparison.
    /// </summary>
    /// <remarks>
    /// The scorer builds a full explanation rather than reading a number off the item, and it was
    /// being called from inside the comparator - roughly nine times the work for five hundred
    /// items. Counting calls is the only way to pin this: the output order is identical either
    /// way, so nothing else would notice.
    /// </remarks>
    [Fact]
    public void EveryItemIsScoredExactlyOnce()
    {
        var items = Enumerable.Range(0, 50)
            .Select(i => new ContentItem { Id = $"c-{i:D2}", Title = $"Item {i}" })
            .ToList();

        var calls = 0;
        var ranked = Ranking.ByRelevance(items, item =>
        {
            calls++;
            return item.Title.Length;
        });

        Assert.Equal(50, ranked.Count);
        Assert.Equal(50, calls);
    }

    [Fact]
    public void ContentIsOrderedByScoreThenNewestFirst()
    {
        var older = new ContentItem { Id = "older", PublishedAt = new DateTime(2026, 1, 1) };
        var newer = new ContentItem { Id = "newer", PublishedAt = new DateTime(2026, 6, 1) };
        var best = new ContentItem { Id = "best", PublishedAt = new DateTime(2025, 1, 1) };

        var ranked = Ranking.ByRelevance(
            [older, newer, best],
            item => item.Id == "best" ? 100 : 10);

        Assert.Equal(["best", "newer", "older"], ranked.Select(i => i.Id));
    }

    [Fact]
    public void TiedContentIsOrderedTheSameWayEveryTime()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => new ContentItem { Id = $"c-{i:D2}", PublishedAt = new DateTime(2026, 1, 1) })
            .ToList();

        var once = Ranking.ByRelevance(items, _ => 5).Select(i => i.Id);
        var again = Ranking.ByRelevance(Enumerable.Reverse(items), _ => 5).Select(i => i.Id);

        Assert.Equal(once, again);
    }
}

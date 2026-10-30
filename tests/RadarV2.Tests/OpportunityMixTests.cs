using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A day's opportunities should include ones the learner can actually take.
/// </summary>
/// <remarks>
/// The batch was assembled by score alone, and every source but one is global, pan-African or
/// remote. So the highest-scoring twelve were almost always foreign: a fellowship in Berlin, a
/// scholarship to study in the United States, an accelerator in London. Each a real opportunity
/// and each a long shot, offered to somebody in Lagos who also needs to know what is open to them
/// this month in Lagos.
///
/// Radar is sold to Nigerians. A page of foreign opportunities is not wrong so much as useless at
/// volume: the reader cannot act on most of it, and the few things they could act on are buried.
/// </remarks>
public class OpportunityMixTests
{
    private sealed record Opening(string Name, bool Nearby);

    private static List<Opening> Ranked(int foreign, int nearby)
    {
        // Foreign first, which is the order that caused the problem: by score alone they fill the
        // page before anything local is reached.
        var list = new List<Opening>();
        for (var i = 0; i < foreign; i++) list.Add(new Opening($"foreign-{i}", Nearby: false));
        for (var i = 0; i < nearby; i++) list.Add(new Opening($"nearby-{i}", Nearby: true));
        return list;
    }

    private static List<Opening> Curate(List<Opening> ranked, int size = 12) =>
        OpportunityMix.Curate(ranked, o => o.Nearby, size);

    /// <summary>
    /// The exact shape that was reaching readers: twelve places, twenty foreign matches ranked
    /// above anything local, and nothing near them on the page.
    /// </summary>
    [Fact]
    public void ADayIsNotEntirelyForeignWhenThereIsSomethingNearby()
    {
        var chosen = Curate(Ranked(foreign: 20, nearby: 6));

        Assert.Equal(12, chosen.Count);
        Assert.Equal(OpportunityMix.ReservedForNearby, chosen.Count(o => o.Nearby));
    }

    /// <summary>The best matches still lead.</summary>
    /// <remarks>
    /// This reserves places; it does not reorder. A learner whose strongest match is an
    /// international fellowship should still see it first.
    /// </remarks>
    [Fact]
    public void TheStrongestMatchesStillComeFirst()
    {
        var chosen = Curate(Ranked(foreign: 20, nearby: 6));

        Assert.Equal("foreign-0", chosen[0].Name);
        Assert.Equal("foreign-1", chosen[1].Name);
    }

    /// <summary>
    /// A learner with nothing near them gets a full page, not a short one.
    /// </summary>
    /// <remarks>
    /// The reserve is drawn on only when something qualifies for it. Holding places open for
    /// opportunities that do not exist would punish the reader for where they live, which is the
    /// opposite of the point.
    /// </remarks>
    [Fact]
    public void NothingNearbyStillFillsTheDay()
    {
        var chosen = Curate(Ranked(foreign: 20, nearby: 0));

        Assert.Equal(12, chosen.Count);
        Assert.All(chosen, o => Assert.False(o.Nearby));
    }

    /// <summary>And fewer nearby than the reserve fills the rest from elsewhere.</summary>
    [Fact]
    public void FewerNearbyThanTheReserveStillFillsTheDay()
    {
        var chosen = Curate(Ranked(foreign: 20, nearby: 2));

        Assert.Equal(12, chosen.Count);
        Assert.Equal(2, chosen.Count(o => o.Nearby));
    }

    /// <summary>
    /// A day that is mostly local stays mostly local.
    /// </summary>
    /// <remarks>
    /// The reserve is a floor, not a quota. Somebody whose matches are nearly all Nigerian should
    /// get nearly all Nigerian results, not four.
    /// </remarks>
    [Fact]
    public void AMostlyLocalDayIsNotCutBackToTheReserve()
    {
        var chosen = Curate(Ranked(foreign: 2, nearby: 20));

        Assert.Equal(12, chosen.Count);
        Assert.True(chosen.Count(o => o.Nearby) >= 8, "A local-heavy day was cut back too far.");
    }

    /// <summary>
    /// And a corpus that is almost entirely local still shows something else.
    /// </summary>
    /// <remarks>
    /// The reserve cut one way only to begin with, which is not a mix. One source arriving with
    /// several hundred Nigerian job listings makes every candidate nearby, and a learner looking
    /// for work would never see a scholarship or a fellowship again - the opportunities the
    /// product was built around, pushed off the page by the fix for the opposite problem.
    /// </remarks>
    [Fact]
    public void ADayIsNotEntirelyLocalEither()
    {
        var chosen = Curate(Ranked(foreign: 6, nearby: 40));

        Assert.Equal(12, chosen.Count);
        Assert.True(
            chosen.Count(o => !o.Nearby) >= OpportunityMix.ReservedForNearby,
            $"Only {chosen.Count(o => !o.Nearby)} of the day came from further afield.");
    }

    /// <summary>Nearby items beyond the batch size do not crowd out the rest.</summary>
    [Fact]
    public void MoreNearbyThanADayHoldsStillLeavesRoom()
    {
        var chosen = Curate(Ranked(foreign: 1, nearby: 100));

        Assert.Equal(12, chosen.Count);
        Assert.Equal(1, chosen.Count(o => !o.Nearby));
    }

    /// <summary>Nothing is dropped when there is less than a full day.</summary>
    [Fact]
    public void AShortDayIsReturnedWhole()
    {
        var ranked = Ranked(foreign: 3, nearby: 1);

        Assert.Equal(4, Curate(ranked).Count);
    }

    /// <summary>And nothing is ever duplicated or invented.</summary>
    [Fact]
    public void EveryChosenItemCameFromTheList()
    {
        var ranked = Ranked(foreign: 20, nearby: 6);
        var chosen = Curate(ranked);

        Assert.Equal(chosen.Count, chosen.Distinct().Count());
        Assert.All(chosen, o => Assert.Contains(o, ranked));
    }

    // ── What counts as near somebody ────────────────────────────────────────

    /// <summary>Remote is near everybody, which is the point of remote.</summary>
    [Fact]
    public void RemoteIsNearEverybody()
    {
        Assert.True(OpportunityMix.IsNearby("Global", isRemote: true, where: "Lagos, Nigeria"));
        Assert.True(OpportunityMix.IsNearby("Remote", isRemote: false, where: ""));
    }

    /// <summary>Their own country counts, matched either way round.</summary>
    /// <remarks>
    /// A profile says "Lagos, Nigeria" and the tag says "Nigeria"; a profile says "Nigeria" and
    /// the tag could say the same. Neither is a prefix of the other in both directions, so both
    /// directions are checked.
    /// </remarks>
    [Fact]
    public void TheirOwnCountryCounts()
    {
        Assert.True(OpportunityMix.IsNearby("Nigeria", isRemote: false, where: "Lagos, Nigeria"));
        Assert.True(OpportunityMix.IsNearby("Nigeria", isRemote: false, where: "Nigeria"));
    }

    /// <summary>The continent counts. Somewhere else does not.</summary>
    [Fact]
    public void AfricaCountsAndGlobalDoesNot()
    {
        Assert.True(OpportunityMix.IsNearby("Africa", isRemote: false, where: "Lagos, Nigeria"));
        Assert.False(OpportunityMix.IsNearby("Global", isRemote: false, where: "Lagos, Nigeria"));
        Assert.False(OpportunityMix.IsNearby("Ghana", isRemote: false, where: "Lagos, Nigeria"));
    }

    /// <summary>
    /// A learner who has not said where they are is not assumed to be anywhere.
    /// </summary>
    /// <remarks>
    /// Radar asks rather than infers. Guessing Nigeria because most users are Nigerian would put
    /// Lagos internships in front of somebody in Accra, with a sentence saying they are near.
    /// </remarks>
    [Fact]
    public void SomebodyWhoHasNotSaidWhereTheyAreIsNotGuessedAt()
    {
        Assert.False(OpportunityMix.IsNearby("Nigeria", isRemote: false, where: ""));
        Assert.False(OpportunityMix.IsNearby("Nigeria", isRemote: false, where: null));
    }
}

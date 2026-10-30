namespace RadarV2.Services;

/// <summary>
/// Making sure a day's opportunities include ones the learner can actually take.
/// </summary>
/// <remarks>
/// The batch was assembled by score alone. Every source but one is global, pan-African or
/// remote, so the highest-scoring twelve were almost always foreign — a fellowship in Berlin, a
/// scholarship to study in the United States, an accelerator in London. Each a real opportunity
/// and each a long shot, offered to somebody in Lagos who also needs to know what is open to them
/// this month in Lagos.
///
/// Radar is sold to Nigerians. A page of foreign opportunities is not wrong so much as useless at
/// volume: the reader cannot act on most of it, and the few things they could act on are buried
/// underneath.
///
/// So the day reserves places. Not a filter and not a demotion — the best matches still lead, and
/// nothing local is added that does not match the learner. It only guarantees that when Radar
/// holds something near them, some of it is on the page.
/// </remarks>
public static class OpportunityMix
{
    /// <summary>
    /// How many of a day's opportunities are kept for ones near the reader.
    /// </summary>
    /// <remarks>
    /// A third. Enough that the page is never entirely foreign, small enough that a strong
    /// international match is not pushed off by a weaker local one.
    /// </remarks>
    public const int ReservedForNearby = 4;

    /// <summary>
    /// The regions a learner can act on from where they are.
    /// </summary>
    /// <remarks>
    /// Their own country first, then the continent, then anything remote — remote is near
    /// everybody, which is the whole point of it. Global is not: an opportunity tagged Global is
    /// usually one that happens somewhere else.
    /// </remarks>
    public static bool IsNearby(string? regionTag, bool isRemote, string? where)
    {
        if (isRemote) return true;

        var region = (regionTag ?? string.Empty).Trim();
        if (region.Length == 0) return false;

        if (region.Equals("Remote", StringComparison.OrdinalIgnoreCase)) return true;
        if (region.Equals("Africa", StringComparison.OrdinalIgnoreCase)) return true;

        // Their own country, when they have said where they are. Matched both ways so that a
        // profile saying "Lagos, Nigeria" and a tag saying "Nigeria" find each other.
        var home = (where ?? string.Empty).Trim();
        if (home.Length == 0) return false;

        return home.Contains(region, StringComparison.OrdinalIgnoreCase)
            || region.Contains(home, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A day's worth, with places kept for what is near the reader.
    /// </summary>
    /// <remarks>
    /// Ranked order is preserved inside both groups, so the best match still leads and the
    /// nearby ones arrive in the order they scored. The reserve is only drawn on when something
    /// qualifies for it: a learner with nothing local near them gets the same twelve they would
    /// have had, rather than a short page.
    /// </remarks>
    public static List<T> Curate<T>(
        IReadOnlyList<T> ranked,
        Func<T, bool> isNearby,
        int size,
        int reserved = ReservedForNearby)
    {
        if (ranked.Count <= size) return [.. ranked];

        reserved = Math.Clamp(reserved, 0, size);

        var nearby = new List<T>();
        var elsewhere = new List<T>();

        foreach (var item in ranked)
        {
            (isNearby(item) ? nearby : elsewhere).Add(item);
        }

        // Whatever the top of the list would have given anyway, minus the places being kept.
        var openPlaces = size - Math.Min(reserved, nearby.Count);

        // And the same floor the other way round. A reserve on one side only is not a mix: one
        // source of several hundred local jobs makes every item nearby, and a learner looking
        // for work would never see the scholarships and fellowships again. Kept symmetric so
        // that whichever side the corpus is heavy on, the other still reaches the page.
        var nearbyPlaces = size - Math.Min(reserved, elsewhere.Count);

        var chosen = new List<T>(size);
        var takenNearby = 0;
        var takenElsewhere = 0;

        foreach (var item in ranked)
        {
            if (chosen.Count == size) break;

            if (isNearby(item))
            {
                if (takenNearby < nearbyPlaces)
                {
                    chosen.Add(item);
                    takenNearby++;
                }

                continue;
            }

            if (takenElsewhere < openPlaces)
            {
                chosen.Add(item);
                takenElsewhere++;
            }
        }

        // The page is still full when one side ran out before its places did.
        if (chosen.Count < size)
        {
            foreach (var item in ranked)
            {
                if (chosen.Count == size) break;
                if (!chosen.Contains(item)) chosen.Add(item);
            }
        }

        return chosen;
    }
}

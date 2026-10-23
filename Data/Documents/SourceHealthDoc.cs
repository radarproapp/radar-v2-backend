namespace RadarV2.Data.Documents;

/// <summary>
/// What each ingestion source last did, kept so the question can be answered instead of guessed.
/// </summary>
/// <remarks>
/// Twenty sources were added and produced nothing for an hour. Finding out why took four rounds:
/// the interest-to-layer map sent them to a layer their own learners could not see; items were
/// enriched source by source so the end of the registry was never reached; fetching ran
/// sequentially across 258 hosts; and a feed returning 403 returned an empty list silently,
/// indistinguishable from a feed that published nothing that day.
///
/// Every one of those was invisible. There was no way to ask "is InfoQ working?" — only to add a
/// source, wait, probe the feed, and infer. This exists so that the next time the feed is missing
/// something, the answer is one request rather than an afternoon.
/// </remarks>
public class SourceHealthDoc
{
    /// <summary>The source id from the registry, so the record is stable across restarts.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;

    public DateTime? LastAttemptAt { get; set; }

    /// <summary>When this source last produced at least one item. Null means it never has.</summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>The HTTP status of the last fetch. 0 when the request never completed.</summary>
    public int LastHttpStatus { get; set; }

    /// <summary>Items the feed offered on the last fetch, before deduplication.</summary>
    public int LastItemsFetched { get; set; }

    /// <summary>Of those, how many Radar had not seen before.</summary>
    public int LastItemsNew { get; set; }

    /// <summary>How many items from this source Radar has stored in total.</summary>
    public long TotalItemsStored { get; set; }

    /// <summary>Why the last attempt produced nothing, when it produced nothing.</summary>
    public string? LastError { get; set; }

    /// <summary>Consecutive attempts that produced no items — the signal that a source has died.</summary>
    public int ConsecutiveEmpty { get; set; }
}

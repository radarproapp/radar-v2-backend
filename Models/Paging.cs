using System.Text;

namespace RadarV2.Models;

/// <summary>
/// One page of a list response. <see cref="NextCursor"/> is null when there is nothing after this
/// page, so a client can stop on null rather than guessing from a short page.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }

    /// <summary>
    /// Why the page came back empty, when it did. <see cref="FeedEmptyReason.NotEmpty"/> otherwise.
    ///
    /// The client cannot work this out for itself: an empty list looks identical whether the
    /// corpus is empty, the user filtered to a type nothing matches, or they dismissed everything.
    /// It used to guess, and told every one of those users "Radar is still curating content for
    /// your interests" - false in two cases out of three, and offering a Refresh button that could
    /// not possibly help.
    /// </summary>
    public FeedEmptyReason EmptyReason { get; set; } = FeedEmptyReason.NotEmpty;
}

/// <summary>Why a feed page has nothing in it. Ordered from "nothing exists" to "nothing new".</summary>
public enum FeedEmptyReason
{
    NotEmpty,

    /// <summary>No content has been ingested at all. A new deployment, or ingestion is not running.</summary>
    NoContentAtAll,

    /// <summary>Content exists, but none of the type the user filtered to.</summary>
    NoneOfThisType,

    /// <summary>The user has dismissed everything that would otherwise match.</summary>
    EverythingDismissed,

    /// <summary>Content exists and nothing is filtered out; there is simply nothing new right now.</summary>
    NothingNewRightNow,
}

/// <summary>
/// Opaque forward cursor for keyset pagination.
///
/// Keyset (rather than offset) pagination matters here because the feed is a live, constantly
/// growing collection: with <c>Skip(n)</c> every insert between two requests shifts the window and
/// the reader silently sees duplicates or misses rows. A cursor encodes the exact row to resume
/// after, so that cannot happen.
///
/// The cursor carries the sort key <em>and</em> the item id. The id is not decoration: two items can
/// share a PublishedAt (bulk ingestion always does), and without a unique tiebreaker a timestamp tie
/// either repeats a row or skips one at the page boundary.
/// </summary>
public static class Cursor
{
    /// <summary>Encodes a sort position. Opaque to clients — treat it as an unreadable token.</summary>
    public static string Encode(DateTime sortKey, string id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sortKey.Ticks}|{id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? value, out DateTime sortKey, out string id)
    {
        sortKey = default;
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);

            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = raw.IndexOf('|');
            if (separator <= 0 || separator == raw.Length - 1) return false;

            if (!long.TryParse(raw[..separator], out var ticks)) return false;

            sortKey = new DateTime(ticks, DateTimeKind.Utc);
            id = raw[(separator + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

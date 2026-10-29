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

    /// <summary>
    /// The learner has not personalised yet, so there is nothing that is theirs to show.
    /// </summary>
    /// <remarks>
    /// Distinct from having nothing ingested or nothing new: Radar has plenty, it simply has no
    /// basis for choosing any of it. Saying that plainly is the point — a generic feed under a
    /// heading promising relevance teaches a new user that Radar does not know them.
    /// </remarks>
    NotPersonalisedYet,

    /// <summary>No content has been ingested at all. A new deployment, or ingestion is not running.</summary>
    NoContentAtAll,

    /// <summary>Content exists, but none of the type the user filtered to.</summary>
    NoneOfThisType,

    /// <summary>The user has dismissed everything that would otherwise match.</summary>
    EverythingDismissed,

    /// <summary>Content exists and nothing is filtered out; there is simply nothing new right now.</summary>
    NothingNewRightNow,

    /// <summary>
    /// Radar holds plenty, and none of it mentions anything the reader told us.
    /// </summary>
    /// <remarks>
    /// This is the state the strand filter creates and the enum had no word for, so it was
    /// reported as one of two things that were both untrue: that the reader had dismissed
    /// everything — offering a "bring them back" button that cannot help — or that they were up
    /// to date, when the library is full of material that simply does not match their words.
    ///
    /// Which is the same dishonesty as the catch-all this replaced, moved into the empty state.
    /// A feed that shows nothing and says why is honest; one that shows nothing and blames the
    /// reader for it is not.
    ///
    /// The action that fits this is widening what they follow, not checking again.
    /// </remarks>
    NothingMatchesYourTerms,
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

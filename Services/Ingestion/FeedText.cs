using System.Net;
using System.Text.RegularExpressions;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Turning what a source sends into something safe to show.
/// </summary>
/// <remarks>
/// Shared because it was copied. The RSS path cleaned its descriptions and the TalentApply path
/// did not, which put the one source Radar is actually permitted to store descriptions from in
/// the position of being the only one rendering raw ATS markup at a reader - literal
/// &lt;p&gt; tags and &amp;amp; entities in the feed, from the source the rights gate exists to
/// let through.
/// </remarks>
public static partial class FeedText
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Decodes entities, strips markup and collapses whitespace.</summary>
    /// <remarks>
    /// Decode first, then strip. The copy this replaced did it the other way round, which is the
    /// order that lets markup through: <c>&amp;lt;script&amp;gt;</c> survives the strip untouched
    /// because it contains no angle brackets yet, and the decode that follows turns it back into
    /// a real tag in the stored text. Decoding first means anything that was hiding behind an
    /// entity is a tag by the time the strip runs, and goes.
    ///
    /// One pass, not a loop to a fixed point. Double-encoded input still leaves a visible entity
    /// rather than a tag, which is the safe direction to be wrong in.
    /// </remarks>
    public static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Whitespace().Replace(HtmlTags().Replace(WebUtility.HtmlDecode(value), " "), " ").Trim();

    /// <summary>
    /// Cuts to a length without splitting a character in half.
    /// </summary>
    /// <remarks>
    /// A plain substring cuts by UTF-16 unit, not by character, so a title ending in an emoji or
    /// any astral character stores a lone surrogate - which renders as the replacement glyph and
    /// is invalid text to anything downstream that validates. Job titles carry emoji more often
    /// than one would hope.
    /// </remarks>
    public static string Shorten(string? value, int most)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= most) return value;

        var cut = most;

        // Never end on a high surrogate: its partner is the character after the cut.
        if (char.IsHighSurrogate(value[cut - 1])) cut--;

        return value[..cut].TrimEnd() + "…";
    }
}

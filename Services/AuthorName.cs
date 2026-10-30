namespace RadarV2.Services;

/// <summary>
/// An author's name in the form a reference list wants it.
/// </summary>
/// <remarks>
/// OpenAlex reports a display name - "Ashish Vaswani" - and both APA and Harvard want the
/// surname first with initials after it. Every citation Radar produced used the display name
/// as it stood, so an APA reference list read "Ashish Vaswani &amp; Noam Shazeer (2017)" where it
/// should read "Vaswani, A., &amp; Shazeer, N. (2017)". A student pastes that into a bibliography
/// and it is marked wrong.
///
/// Nothing is invented here. Where a name cannot be taken apart with confidence it is returned
/// as it came, which is a name in the wrong order rather than a different person's name.
/// </remarks>
public static class AuthorName
{
    /// <summary>Suffixes that are not the surname, however last they appear.</summary>
    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "jr.", "sr", "sr.", "ii", "iii", "iv", "phd", "ph.d.", "md", "m.d.",
    };

    /// <summary>
    /// "Ashish Vaswani" becomes "Vaswani, A." - the form APA and Harvard both use.
    /// </summary>
    /// <remarks>
    /// A name that already contains a comma is left alone: the source has given it inverted
    /// and re-inverting would produce a surname out of an initial.
    /// </remarks>
    public static string Inverted(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (trimmed.Contains(',')) return trimmed;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0];

        // A trailing suffix belongs after the initials, not in front of them.
        var suffix = string.Empty;
        if (Suffixes.Contains(parts[^1]) && parts.Length > 2)
        {
            suffix = parts[^1];
            parts = parts[..^1];
        }

        var surname = parts[^1];
        var initials = string.Join(" ", parts[..^1].Select(Initial).Where(i => i.Length > 0));

        if (initials.Length == 0) return trimmed;

        return suffix.Length > 0
            ? $"{surname}, {initials}, {suffix}"
            : $"{surname}, {initials}";
    }

    /// <summary>
    /// "Jean-Luc" becomes "J.-L.", "Ashish" becomes "A.", "A." stays "A.".
    /// </summary>
    private static string Initial(string given)
    {
        if (given.Length == 0) return string.Empty;

        // Already initials, however they were punctuated.
        if (given.Replace(".", string.Empty).Length <= 1)
            return given.EndsWith('.') ? given : $"{given}.";

        // A hyphenated given name keeps its hyphen: Jean-Luc is J.-L., not J.
        if (given.Contains('-'))
        {
            var halves = given.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Where(half => char.IsLetter(half[0]))
                .Select(half => $"{char.ToUpperInvariant(half[0])}.");

            return string.Join("-", halves);
        }

        return char.IsLetter(given[0]) ? $"{char.ToUpperInvariant(given[0])}." : string.Empty;
    }
}

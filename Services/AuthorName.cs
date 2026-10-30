namespace RadarV2.Services;

/// <summary>
/// An author's name in the form a reference list wants it.
/// </summary>
/// <remarks>
/// OpenAlex reports a display name - "Ashish Vaswani" - and APA, Harvard, MLA and Chicago all
/// want the surname first. Every citation Radar produced used the display name as it stood, so
/// an APA reference list read "Ashish Vaswani &amp; Noam Shazeer (2017)" where it must read
/// "Vaswani, A., &amp; Shazeer, N. (2017)". A student pastes that into a bibliography and it is
/// marked wrong.
///
/// Nothing is invented here. Where a name cannot be taken apart with confidence it is returned
/// as it came, which is a name in the wrong order rather than a different person's name.
/// </remarks>
public static class AuthorName
{
    /// <summary>
    /// Words that belong to the surname however far from the end they sit.
    /// </summary>
    /// <remarks>
    /// The first version took the last word as the surname and initialised everything before it,
    /// so "Jan van der Berg" became "Berg, J. V. D." - not an abbreviation of his name but a
    /// different one. The same for "Pieter de Vries", "Marie van Rooyen", "Juan Carlos de la
    /// Cruz", "Ahmed bin Saeed" and "Ngugi wa Thiong'o". Dutch and Afrikaans surnames are
    /// ordinary in South African and Namibian scholarship, Arabic ones across North and West
    /// Africa, and the class promised it would not take a name apart it could not take apart.
    /// </remarks>
    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase)
    {
        "van", "von", "der", "den", "de", "del", "della", "di", "da", "dos", "das", "du",
        "le", "la", "les", "ter", "ten", "bin", "ibn", "al", "el", "wa", "abu", "af", "av",
        "mac", "mc", "st", "st.", "san", "santa", "bint", "ben",
    };

    /// <summary>Suffixes that are part of the name but are not the surname.</summary>
    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "jr.", "sr", "sr.", "ii", "iii", "iv", "v",
    };

    /// <summary>
    /// Degrees, which APA 7 omits from a reference entry entirely (§9.8).
    /// </summary>
    /// <remarks>
    /// These were treated as suffixes and kept, so "Jane Smith MD" cited as "Smith, J., MD".
    /// A degree is not part of how a work is cited.
    /// </remarks>
    private static readonly HashSet<string> Degrees = new(StringComparer.OrdinalIgnoreCase)
    {
        "phd", "ph.d.", "ph.d", "md", "m.d.", "m.d", "msc", "m.sc.", "mba", "dphil",
        "frcp", "facs", "rn", "do", "dds", "pharmd", "edd", "jd", "llm",
    };

    /// <summary>
    /// Titles, which are not names. "Dr Ashish Vaswani" was becoming "Vaswani, D. A." - an
    /// initial manufactured out of a courtesy title.
    /// </summary>
    private static readonly HashSet<string> Honorifics = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "dr.", "prof", "prof.", "professor", "mr", "mr.", "mrs", "mrs.", "ms", "ms.",
        "miss", "sir", "dame", "rev", "rev.",
    };

    /// <summary>
    /// "Ashish Vaswani" becomes "Vaswani, A." - the form APA and Harvard both use.
    /// </summary>
    /// <remarks>
    /// A name that already contains a comma is left alone: the source has given it inverted, and
    /// re-inverting would make a surname out of an initial.
    /// </remarks>
    public static string Inverted(string? name) => Formatted(name, initials: true);

    /// <summary>
    /// "Ashish Vaswani" becomes "Vaswani, Ashish" - the form MLA and Chicago use for the first
    /// author of an entry.
    /// </summary>
    public static string InvertedInFull(string? name) => Formatted(name, initials: false);

    /// <summary>
    /// "Ashish Vaswani" stays "Ashish Vaswani", minus any title or degree - the form MLA and
    /// Chicago use for every author after the first.
    /// </summary>
    public static string AsWritten(string? name)
    {
        var parts = Meaningful(name);
        return parts.Count == 0 ? (name ?? string.Empty).Trim() : string.Join(" ", parts);
    }

    /// <summary>"Ashish Vaswani" becomes "A. Vaswani" - the form IEEE uses.</summary>
    public static string InitialsFirst(string? name)
    {
        var parts = Meaningful(name);
        if (parts.Count < 2) return AsWritten(name);

        var (surname, given) = SplitSurname(parts);
        if (given.Count == 0) return surname;

        return $"{string.Join(" ", given.Select(Initial).Where(i => i.Length > 0))} {surname}";
    }

    private static string Formatted(string? name, bool initials)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (trimmed.Contains(',')) return trimmed;

        // A name written in a script with no Latin letters is already family-first - Chinese,
        // Japanese and Korean names among them - so swapping it round produces a different
        // person, and an "initial" of a logograph is meaningless. APA 7 keeps them as given.
        if (!trimmed.Any(char.IsLetter) || !trimmed.Any(c => c is >= 'A' and <= 'z' && char.IsLetter(c)))
            return trimmed;

        var parts = Meaningful(trimmed);
        if (parts.Count == 0) return trimmed;
        if (parts.Count == 1) return parts[0];

        // A trailing suffix belongs after the name, not inside it.
        var suffix = string.Empty;
        if (Suffixes.Contains(parts[^1]))
        {
            suffix = parts[^1];
            parts = parts[..^1];

            // "Vaswani Jr." is a surname and a suffix with no given name to initialise.
            if (parts.Count == 1) return $"{parts[0]}, {suffix}";
            if (parts.Count == 0) return trimmed;
        }

        var (surname, given) = SplitSurname(parts);
        if (given.Count == 0) return suffix.Length > 0 ? $"{surname}, {suffix}" : surname;

        var rendered = initials
            ? string.Join(" ", given.Select(Initial).Where(i => i.Length > 0))
            : string.Join(" ", given);

        if (rendered.Length == 0) return trimmed;

        return suffix.Length > 0
            ? $"{surname}, {rendered}, {suffix}"
            : $"{surname}, {rendered}";
    }

    /// <summary>
    /// The surname, taking any particles that lead into it, and what is left in front.
    /// </summary>
    private static (string Surname, List<string> Given) SplitSurname(List<string> parts)
    {
        var start = parts.Count - 1;

        // Walk back over particles, but never over all of them - a name that is nothing but
        // particles has no given name to separate, and taking the lot would leave nothing.
        while (start > 1 && Particles.Contains(parts[start - 1])) start--;

        var surname = string.Join(" ", parts[start..]);
        return (Capitalised(surname), parts[..start]);
    }

    /// <summary>"van der berg" is written "Van der Berg" when it leads an entry.</summary>
    private static string Capitalised(string surname)
    {
        if (surname.Length == 0 || !char.IsLower(surname[0])) return surname;

        return char.ToUpperInvariant(surname[0]) + surname[1..];
    }

    /// <summary>The words of a name that are part of it: no titles, no degrees.</summary>
    private static List<string> Meaningful(string? name) =>
        [.. (name ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !Honorifics.Contains(word) && !Degrees.Contains(word.TrimEnd(',')))];

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

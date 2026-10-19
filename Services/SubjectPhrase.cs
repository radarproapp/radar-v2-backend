using System.Text.RegularExpressions;

namespace RadarV2.Services;

/// <summary>
/// Turns a goal the user typed into a short noun phrase that reads naturally inside a sentence.
///
/// Goals arrive as whole sentences — "Become a hospital nurse manager" — and templates drop them
/// straight into titles, producing "Try Become a hospital nurse manager on something small". Users
/// read that as broken software, and rightly so.
/// </summary>
public static partial class SubjectPhrase
{
    [GeneratedRegex(@"^\s*(i\s+want\s+to\s+|i\s+would\s+like\s+to\s+)?(become|get|be|work)\s+(a|an|as\s+a|as\s+an)\s+",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeadingRoleVerb();

    [GeneratedRegex(@"^\s*(learn|study|master|move\s+into|switch\s+to|transition\s+to|break\s+into|start)\s+",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLearnVerb();

    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex Whitespace();

    /// <summary>
    /// A short phrase naming the subject. Falls back to the original text rather than returning
    /// something empty — a slightly long title beats a blank one.
    /// </summary>
    public static string Clean(string? goal, string? fallbackInterest = null)
    {
        var text = (goal ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) text = (fallbackInterest ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return "this field";

        // "Cybersecurity: Learn New Skills" -> "Cybersecurity"
        var colon = text.IndexOf(':');
        if (colon > 2) text = text[..colon].Trim();

        text = LeadingRoleVerb().Replace(text, string.Empty);
        text = LeadingLearnVerb().Replace(text, string.Empty);
        text = Whitespace().Replace(text, " ").Trim().TrimEnd('.', ',');

        if (string.IsNullOrWhiteSpace(text)) return (goal ?? fallbackInterest ?? "this field").Trim();

        // Keep it short enough to sit inside a sentence.
        var words = text.Split(' ');
        if (words.Length > 6) text = string.Join(" ", words.Take(6));

        return text;
    }

    /// <summary>Lowercased for use mid-sentence, unless it looks like a proper noun or acronym.</summary>
    public static string Lower(string? goal, string? fallbackInterest = null)
    {
        var cleaned = Clean(goal, fallbackInterest);
        var first = cleaned.Split(' ')[0];

        // Leave "SQL", "AI", "Power BI" alone.
        if (first.Length <= 4 && first.ToUpperInvariant() == first) return cleaned;

        return char.ToLowerInvariant(cleaned[0]) + cleaned[1..];
    }
}

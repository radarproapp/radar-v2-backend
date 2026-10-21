using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// The one-line "why you're seeing this" that sits on every feed card and home-screen signal.
///
/// This lived in two places — once on the server for the home brief, and once again in the feed
/// page's own `formatWhySeeing` — and the two drifted. The client copy pasted the raw goal into a
/// noun slot ("Relevant to your goal of \"Get Promotion\" in AI Fundamentals"), printed matched
/// signals with their internal dimension prefix still attached ("your interest in Goal: AI"), and
/// never deduped, so the same word appeared twice in one sentence.
///
/// Computed server-side now, next to the scorer that produces the signals, so there is one
/// implementation and the card cannot disagree with the brief it links to.
/// </summary>
public static class WhyShown
{
    /// <summary>
    /// Names the dimensions the scorer actually fired on, in the user's own words. Falls back to
    /// stating plainly that the item is recent — never to an asserted connection nothing computed.
    /// </summary>
    public static string Build(ContentItem item, IReadOnlyList<string> matchedSignals)
    {
        // Dedupe on the underlying phrase, not the formatted sentence: a user's goal domain and
        // their stated interest are frequently the same word.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var phrases = matchedSignals
            .Select(signal =>
            {
                var split = signal.IndexOf(':');
                if (split < 0) return seen.Add(signal.Trim()) ? signal.Trim() : string.Empty;

                var dimension = signal[..split].Trim();
                var phrase = signal[(split + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(phrase)) return string.Empty;
                if (!seen.Add(phrase)) return string.Empty;

                return dimension switch
                {
                    "Goal" => $"your goal: {phrase}",
                    "Interest" => $"your interest in {phrase}",
                    "Capability" => $"your {phrase} experience",
                    "Problem" => $"what you said is holding you back ({phrase})",
                    "Intent" => $"what you're working on right now ({phrase})",
                    "Learning" => $"what you're learning next ({phrase})",
                    "Role" => $"the role you're aiming for ({phrase})",
                    "Gap" => $"something you're still building ({phrase})",
                    _ => phrase,
                };
            })
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Take(2)
            .ToList();

        if (phrases.Count > 0) return $"Matches {string.Join(" and ", phrases)}";

        return !string.IsNullOrWhiteSpace(item.Topic)
            ? $"Recent in {item.Topic}"
            : $"Recent from {item.Source}";
    }
}

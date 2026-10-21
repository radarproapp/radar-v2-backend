namespace RadarV2.Services;

/// <summary>
/// The words that mean the same field, held once.
///
/// A learner who types "cybersecurity" should still match an opportunity that only says "SOC
/// analyst", and one who types "nursing" should match "nurse". Each place that needed this grew
/// its own keyword branch, and because cybersecurity was the example everyone worked from, it was
/// the only field that ever got synonyms — so a nursing learner matched nothing and was quietly
/// handed another field's results.
///
/// No field is privileged here. A subject that is not listed still matches on its own words, which
/// is what makes an unlisted field work as well as a listed one rather than falling off a cliff.
/// </summary>
public static class DomainVocabulary
{
    /// <summary>
    /// Fields that have common names a learner is unlikely to type but an advert is likely to use.
    /// Shared with <see cref="StageProjectCatalog"/> so the two cannot drift apart.
    /// </summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cyber"] = ["cyber", "security", "soc", "hacking", "threat", "malware", "forensic", "penetration"],
        ["data"] = ["data", "analytics", "sql", "excel", "bi", "statistic"],
        ["software"] = ["software", "engineer", "developer", "coding", "programming", "backend", "frontend"],
    };

    /// <summary>The keyword list for one known field. Used by the project catalogue.</summary>
    public static string[] For(string key) => Synonyms[key];

    /// <summary>
    /// Every word worth matching on for a subject: the subject's own words, plus the synonyms of a
    /// known field if the subject names one.
    ///
    /// Words shorter than four characters are dropped. "IT" and "AI" are real subjects but match
    /// almost any text as a substring, and a false domain match is worth far more score here than
    /// a missed one.
    /// </summary>
    public static string[] Expand(string? subject)
    {
        var cleaned = (subject ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleaned)) return [];

        var words = cleaned
            .Split([' ', ',', '/', '-', '&', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 4)
            .ToList();

        foreach (var (_, keywords) in Synonyms)
        {
            if (keywords.Any(cleaned.Contains)) words.AddRange(keywords);
        }

        return words.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

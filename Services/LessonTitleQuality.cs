namespace RadarV2.Services;

/// <summary>
/// Rejects lesson titles that are the subject's own name in a costume.
/// </summary>
/// <remarks>
/// A Computer Science student opened their path and was shown three lessons: "What Computer
/// Science actually involves day to day", "Core concepts and vocabulary in Computer Science" and
/// "The standard toolset for Computer Science". Nothing there is wrong, and nothing there is
/// content either — strip the subject out of each one and what remains is filler. A path made of
/// these reads as if it were written without knowing anything about the learner, because it was.
///
/// The test is deliberately conservative: it only fires when the title actually contains the
/// subject. "How TCP handshakes work" is never rejected, because there is nothing to strip and so
/// nothing to judge. That keeps a real lesson safe at the cost of letting a bland one through,
/// which is the right way round.
/// </remarks>
public static class LessonTitleQuality
{
    /// <summary>
    /// Words that carry no information about what a lesson teaches. A title that is nothing but
    /// the subject plus these is not naming a topic.
    /// </summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "actually", "all", "an", "and", "apply", "applying", "approach", "at",
        "basic", "basics", "begin", "beginner", "beginners", "beginning", "by", "complete",
        "concept", "concepts", "core", "course", "day", "do", "essential", "essentials",
        "everything", "explained", "first", "for", "foundation", "foundations", "from",
        "fundamental", "fundamentals", "get", "getting", "guide", "how", "in", "into",
        "intro", "introduction", "involve", "involves", "is", "it", "key", "know",
        "knowledge", "landscape", "lesson", "level", "levels", "main", "module", "need",
        "needs", "of", "on", "overview", "part", "practical", "practice", "primer",
        "principle", "principles", "quick", "real", "series", "short", "should", "simple",
        "standard", "start", "started", "starting", "step", "steps", "terminology", "the",
        "to", "today", "tool", "toolset", "tools", "topic", "topics", "understand",
        "understanding", "vocabulary", "what", "why", "with", "work", "working", "works",
        "world", "you", "your",
    };

    /// <summary>
    /// Whether this title says nothing beyond naming its subject.
    /// </summary>
    public static bool IsGeneric(string? title, string? subject)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        if (string.IsNullOrWhiteSpace(subject)) return false;

        var remainder = title.Replace(subject, " ", StringComparison.OrdinalIgnoreCase);

        // The subject was not mentioned, so there is no claim of genericness to make.
        if (remainder.Length == title.Length) return false;

        var words = remainder.Split(
            [' ', '\t', '\n', '\r', ':', '-', '–', '—', ',', '.', '(', ')', '/', '&', '\'', '"'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Every remaining word is filler — including the case where the title was the subject and
        // nothing else. A bare number counts as filler so "Cybersecurity 101" does not survive on
        // the strength of its number.
        return words.All(word => Filler.Contains(word) || word.All(char.IsDigit));
    }

    /// <summary>
    /// Drops the titles that name only their subject.
    /// </summary>
    public static List<T> RemoveGeneric<T>(IEnumerable<T> lessons, string subject, Func<T, string?> title) =>
        lessons.Where(lesson => !IsGeneric(title(lesson), subject)).ToList();
}

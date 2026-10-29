using System.Text.RegularExpressions;

namespace RadarV2.Services;

/// <summary>
/// The words that mean the same field, held once.
///
/// A learner who types "cybersecurity" should still match an advert that only says "SOC analyst",
/// and one who types "nursing" should match "nurse". Six services each grew their own copy of this
/// list, and the copies disagreed: "infosec" was only in the Learn Hub's, "forensic" was in three
/// of the others, so the same learner could get a cybersecurity roadmap and a data-analytics Learn
/// Hub on the same profile.
///
/// No field is privileged here. A subject that is not listed matches on its own words, which is
/// what makes an unlisted field work as well as a listed one instead of falling off a cliff.
/// </summary>
public static class DomainVocabulary
{
    /// <summary>
    /// A word that identifies a field, and how much of it has to match.
    ///
    /// Most are prefixes, so "cyber" catches "cybersecurity" and "cyberdefence". Short or ordinary
    /// words have to stand alone, because as substrings they appear everywhere a human would not
    /// expect: plain <c>Contains("soc")</c> matched "social work", "sociology" and "soccer", so a
    /// social-work learner was handed the cybersecurity curriculum. "excel" is the same trap in
    /// the other direction — it is a prefix of "excellent".
    /// </summary>
    private sealed record Keyword(string Text, bool WholeWord = false);

    private static readonly Dictionary<string, Keyword[]> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cyber"] =
        [
            new("cyber"), new("securit"), new("infosec"), new("malware"), new("forensic"),
            new("penetration"), new("hacking"), new("threat"),
            new("soc", WholeWord: true), new("siem", WholeWord: true), new("pentest", WholeWord: true),
        ],
        ["data"] =
        [
            new("analytic"), new("statistic"),
            new("data", WholeWord: true), new("sql", WholeWord: true),
            new("excel", WholeWord: true), new("bi", WholeWord: true),
        ],
        ["ai"] =
        [
            new("artificial"), new("machine learning"), new("neural"),
            new("ai", WholeWord: true), new("llm", WholeWord: true), new("rag", WholeWord: true),
        ],
        ["software"] =
        [
            new("software"), new("engineer"), new("developer"), new("programming"),
            new("backend"), new("frontend"),
            new("coding", WholeWord: true),
        ],
    };

    /// <summary>Does this text name the given field?</summary>
    public static bool Matches(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return Fields.TryGetValue(field, out var keywords) && keywords.Any(k => Mentions(text, k));
    }

    /// <summary>The field this text names, or null. First match wins, so order within <see cref="Fields"/> matters.</summary>
    public static string? FieldOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var (field, keywords) in Fields)
        {
            if (keywords.Any(k => Mentions(text, k))) return field;
        }

        return null;
    }

    /// <summary>
    /// Does <paramref name="haystack"/> talk about <paramref name="subject"/>?
    ///
    /// Matches on the subject's own words, and on the field's synonyms when the subject names a
    /// known field. Words shorter than four characters are ignored: "IT" and "AI" are real
    /// subjects, but as fragments they appear in almost any text, and a false match here is worth
    /// far more score than a missed one.
    /// </summary>
    public static bool MentionsSubject(string? haystack, string? subject)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(subject)) return false;

        var field = FieldOf(subject);
        if (field is not null && Matches(haystack, field)) return true;

        return OwnWords(subject).Any(w => Mentions(haystack, new Keyword(Stem(w))));
    }

    /// <summary>
    /// The stem of a word, so a subject matches the forms an advert actually uses.
    ///
    /// A learner types "Nursing"; the posting says "Nurse". As a prefix, "nursing" matches neither
    /// "nurse" nor "nurses", so the unlisted fields — the ones with no synonym list to fall back
    /// on — were exactly the ones that matched nothing. Trimming the ending fixes the common
    /// cases: nursing/nurse, teaching/teacher, economics/economic, engineering/engineer.
    ///
    /// Only trims when at least four characters survive, so short words are left alone rather than
    /// reduced to a fragment that matches everything.
    /// </summary>
    private static string Stem(string word)
    {
        foreach (var ending in Endings)
        {
            if (word.Length - ending.Length < 4) continue;
            if (word.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) return word[..^ending.Length];
        }

        return word;
    }

    /// <summary>
    /// Longest first, so "ing" is tried before "s" would ever be.
    ///
    /// "ery" earns its place on midwifery/midwife, surgery/surgeon and nursery/nurse. A bare "y"
    /// does not: it turns "policy" into "polic", which matches "police".
    /// </summary>
    private static readonly string[] Endings = ["ing", "ery", "ies", "es", "ed", "s"];

    /// <summary>
    /// Everything worth searching a corpus for when somebody says they follow this subject.
    /// </summary>
    /// <remarks>
    /// The feed matched a reader's interests against item topics, titles and tags using the words
    /// they typed, unaltered. For a typical profile that reached four of the corpus's 337 topic
    /// strings — "Product" matched no topic at all — so titles were carrying the whole filter
    /// alone. The reader was not wrong and the corpus was not missing the subject; the two were
    /// using different words for it.
    ///
    /// Which is the problem this class already existed to solve, for six other services. Two
    /// things it knows that a raw term does not:
    ///
    /// <list type="bullet">
    /// <item><b>Stems.</b> "Nursing" does not match "Nurse" as a prefix, and "Marketing" does not
    /// match "Market". Trimming the ending catches the forms a headline actually uses.</item>
    /// <item><b>The field's other words.</b> Somebody following cybersecurity means items about
    /// malware and penetration testing, which share no letters with what they typed.</item>
    /// </list>
    ///
    /// Bounded, because each of these becomes a regex alternation against unindexed fields, and a
    /// reader who lists many interests should not pay for it twice over.
    /// </remarks>
    public static IEnumerable<string> SearchTerms(string? subject, int max = 6)
    {
        if (string.IsNullOrWhiteSpace(subject)) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var given = 0;

        foreach (var word in OwnWords(subject))
        {
            var stem = Stem(word);
            if (!seen.Add(stem)) continue;

            yield return stem;
            if (++given == max) yield break;
        }

        var field = FieldOf(subject);
        if (field is null || !Fields.TryGetValue(field, out var keywords)) yield break;

        foreach (var keyword in keywords)
        {
            // Whole-word keywords are the ones that are dangerous as prefixes — "soc" matching
            // "soccer" and "sociology" is why they are marked. A query cannot carry that
            // distinction, so they are left out rather than let in as prefixes.
            if (keyword.WholeWord || !seen.Add(keyword.Text)) continue;

            yield return keyword.Text;
            if (++given == max) yield break;
        }
    }

    /// <summary>The subject's own words, long enough to be worth matching on.</summary>
    public static string[] OwnWords(string? subject) =>
        (subject ?? string.Empty)
            .Split([' ', ',', '/', '-', '&', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool Mentions(string text, Keyword keyword)
    {
        // \b at the front always: a keyword has to start a word, so "soc" never matches the middle
        // of a word. The closing \b is what separates "stands alone" from "is a prefix".
        var pattern = keyword.WholeWord
            ? $@"\b{Regex.Escape(keyword.Text)}\b"
            : $@"\b{Regex.Escape(keyword.Text)}";

        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }
}

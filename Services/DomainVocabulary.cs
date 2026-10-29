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
            // "engineer" is not here, and that is the point: it matched civil, mechanical,
            // chemical, petroleum and agricultural engineering, every one of which was then
            // relabelled as software. A civil engineering student was shown frontend jobs with
            // the sentence "It is civil engineering work, which is what you are learning."
            // Software postings say software, developer, programming, backend or frontend.
            new("software"), new("developer"), new("programming"),
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

        return WorthMatching(subject).Any(w => MentionsForm(haystack, w));
    }

    /// <summary>
    /// Words that describe the level of a course rather than its subject.
    /// </summary>
    /// <remarks>
    /// Measured on production. A learner whose path was "foundations of Mathematics" was shown
    /// twelve opportunities at 84 per cent, every one of them from a Foundation - the Heinrich
    /// Böll Foundation, the Jack Kent Cooke Foundation - because "foundations" stems to
    /// "foundation" and the funder's name contains it. Each one carried the sentence "It is
    /// foundations of Mathematics work, which is what you are learning", which is a false
    /// explanation of a false match, stated with a percentage beside it.
    ///
    /// None of these words is the thing being studied. "Foundations of Mathematics" is
    /// mathematics; "Introduction to Law" is law.
    /// </remarks>
    private static readonly HashSet<string> CourseScaffolding = new(StringComparer.OrdinalIgnoreCase)
    {
        // How much of a subject, rather than which subject.
        "foundation", "foundations", "introduction", "introductory", "principle", "principles",
        "fundamental", "fundamentals", "basic", "basics", "essential", "essentials",
        "advanced", "intermediate", "beginner", "beginners", "overview", "general",
        "concept", "concepts", "topic", "topics", "studies", "modern", "contemporary", "applied",

        // The broad word a discipline shares with every other discipline under it. "Civil
        // Engineering" matched "Software Engineer, frontend team" because both say engineer, and
        // the word that tells them apart - civil - was not required. For a qualified subject the
        // qualifier is the subject: mechanical engineering is mechanical, computer science is
        // computer. Alone they still match, because then they are all the learner gave.
        "engineer", "engineering", "science", "sciences", "technology", "management",
    };

    /// <summary>
    /// The words of a subject that actually identify it.
    /// </summary>
    /// <remarks>
    /// Scaffolding is dropped only while something else survives. A subject that is nothing but
    /// these words has to match on them or match on nothing, and matching on nothing would hide
    /// content from somebody whose stated interest Radar had decided was too vague - a judgement
    /// it is not entitled to make silently.
    /// </remarks>
    public static string[] WorthMatching(string? subject)
    {
        var words = OwnWords(subject);

        var distinctive = words
            .Where(w => !CourseScaffolding.Contains(w) && !CourseScaffolding.Contains(Stem(w)))
            .ToArray();

        if (distinctive.Length > 0) return distinctive;

        // Nothing of four letters or more survived, which is what happens when the subject of a
        // course is a short word: "Introduction to Law" is introduction, to, law. Falling back to
        // the framing word would match that path against any advert mentioning an introduction -
        // the same failure as a Foundation, one word along.
        //
        // Three letters is enough here and two is not. Mentions requires whole words, so "law"
        // matches law and not lawn; "AI" and "IT" are the pair that genuinely cannot be told from
        // ordinary text, and OwnWords still drops them.
        var shortSubject = ShortWords(subject);
        if (shortSubject.Length > 0) return shortSubject;

        return words;
    }

    /// <summary>
    /// Three-letter words in a subject that are the subject rather than grammar.
    /// </summary>
    private static readonly HashSet<string> Grammar = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "its", "our", "your", "with", "into", "from",
    };

    private static string[] ShortWords(string? subject) =>
        (subject ?? string.Empty)
            .Split([' ', ',', '/', '-', '&', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length == 3 && !Grammar.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
        // "ies" means "y": policies/policy, studies/study, bodies/body. Trimmed bare it left
        // "polic", which matched "police" - a learner interested in public policies was shown
        // Police Force recruitment. The same hazard the comment below rejects a bare "y" for.
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
        {
            return word[..^3] + "y";
        }

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
    private static readonly string[] Endings = ["ing", "ery", "es", "ed", "s"];

    /// <summary>The subject's own words, long enough to be worth matching on.</summary>
    public static string[] OwnWords(string? subject) =>
        (subject ?? string.Empty)
            .Split([' ', ',', '/', '-', '&', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// How much a word may grow before it is a different word.
    /// </summary>
    /// <remarks>
    /// Three letters covers the endings an advert actually uses: nurse, nurses, nursing;
    /// engineer, engineering; mathematic, mathematics. It does not cover bankruptcy, which is
    /// what "Bank" used to match, or Massachusetts, which is what "mass communication" matched
    /// through its first word. An open-ended prefix treats every longer word that happens to
    /// start the same way as the same subject.
    /// </remarks>
    private const int MostAWordMayGrow = 3;

    /// <summary>
    /// Whether the text uses a form of this word.
    /// </summary>
    /// <remarks>
    /// Both the word as the learner typed it and its stem, each allowed to grow by a few letters
    /// and then required to end. Two forms rather than the stem alone because stemming loses the
    /// plural it came from: "policies" stems to the lemma "policy", which does not match the word
    /// "policies" in an advert, so both are tried.
    ///
    /// Separate from Mentions, which the curated field keywords use with an open prefix on
    /// purpose - "cyber" has to reach "cybersecurity", eight letters along. Those lists are
    /// authored and checked; a word somebody typed about themselves is neither.
    /// </remarks>
    private static bool MentionsForm(string text, string word)
    {
        if (Bounded(text, word)) return true;

        var stem = Stem(word);
        return !string.Equals(stem, word, StringComparison.OrdinalIgnoreCase) && Bounded(text, stem);
    }

    private static bool Bounded(string text, string word) =>
        Regex.IsMatch(
            text,
            $@"\b{Regex.Escape(word)}[a-z]{{0,{MostAWordMayGrow}}}\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

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

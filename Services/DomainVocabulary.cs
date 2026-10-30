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

        // The five below exist because an advert in these fields shares no word with the field
        // name, so no stemming rule could ever reach it. An accounting graduate was not shown
        // "Audit associate, ICAN trainee scheme, Lagos" - the most obvious first job in her
        // profession in Nigeria - because the advert never writes "accounting".
        //
        // Added one failing use case at a time, which is the only discipline that keeps a list
        // like this honest. The class comment above is right that curated vocabularies drift;
        // what stops them is that every entry here can be traced to somebody who was not shown
        // their own field's work.
        ["accounting"] =
        [
            new("account"), new("audit"), new("bookkeep"), new("ledger"),
            new("ican", WholeWord: true), new("acca", WholeWord: true),
        ],
        ["criminology"] =
        [
            new("criminolog"), new("crime"), new("policing"), new("penal"), new("offender"),
        ],
        ["sociology"] =
        [
            // Deliberately not a bare "social": it belongs to Social Work as much as to
            // sociology, and a word two fields share is a word that identifies neither.
            new("sociolog"), new("livelihood"), new("ethnograph"), new("social research"),
        ],
        ["political science"] =
        [
            new("politic"), new("electoral"), new("governance"), new("legislat"),
        ],
        ["microbiology"] =
        [
            // "antimicrobial" is spelled out because there is no word boundary in front of the
            // "microbial" inside it, so a prefix rule can never reach it.
            new("microbiolog"), new("microbial"), new("antimicrobial"), new("pathogen"),
        ],

        // Reached through Aliases rather than by their own spelling, which is what lets them be
        // written in the advert's vocabulary. Each one exists because somebody was not shown the
        // most obvious job in their profession.
        ["civil engineering"] =
        [
            new("civil engineer"), new("site engineer"), new("structural engineer"),
            new("highway"), new("construction"), new("geotechnical"),
        ],
        ["mechanical engineering"] =
        [
            new("mechanical engineer"), new("maintenance engineer"), new("plant engineer"),
            new("hvac", WholeWord: true), new("thermodynamic"),
        ],
        ["electrical engineering"] =
        [
            new("electrical engineer"), new("power distribution"), new("power engineer"),
            new("substation"), new("switchgear"),
        ],
        ["chemical engineering"] =
        [
            new("chemical engineer"), new("process engineer"), new("refinery"),
            new("petrochemical"), new("fertiliser plant"),
        ],
        ["architecture"] =
        [
            new("architect"), new("architectural"), new("building design"), new("draughtsman"),
        ],
        ["agriculture"] =
        [
            new("agricultur"), new("agronom"), new("smallholder"), new("crop"),
            new("livestock"), new("extension programme"),
        ],
        ["mass communication"] =
        [
            new("journalis"), new("broadcast"), new("newsroom"), new("current affairs"),
            new("editorial desk"),
        ],
        ["hospitality"] =
        [
            new("hospitality"), new("hotel"), new("front office"), new("concierge"),
            new("guest relations"),
        ],
        ["fashion"] =
        [
            // Bounded, because an unbounded "fashion" reaches "Fashionable theories of
            // economics" - measured, not guessed.
            new("fashion", WholeWord: true), new("ready-to-wear"), new("atelier"),
            new("textile"), new("garment"),
        ],
        ["human resources"] =
        [
            new("human resource"), new("recruitment"), new("talent acquisition"),
            new("people operations"), new("hr business partner"),
            new("hr", WholeWord: true),
        ],
        ["taxation"] =
        [
            // Whole word, because an unbounded "tax" reaches "Taxi operators protest".
            new("tax", WholeWord: true), new("taxation"), new("transfer pricing"),
            new("vat", WholeWord: true), new("fiscal policy"),
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

        // Named first, identified second.
        //
        // The field a subject belongs to used to be worked out by running the field's own
        // keyword list against the subject string - so a list was reachable only if one of its
        // keywords happened to prefix the field's name. "accounting" works purely because
        // "account" prefixes "Accounting". A list for Agriculture keyed on agronomist and
        // smallholder could never fire, because the word "Agriculture" contains neither.
        //
        // That left eleven fields unable to have a list at all, and they are exactly the ones
        // that need one: a civil engineering graduate was not shown "Site engineer, highway
        // construction, Federal Ministry of Works", because the advert never writes "civil".
        var field = AliasOf(subject) ?? FieldOf(subject);
        if (field is not null && Matches(haystack, field)) return true;

        return WorthMatching(subject).Any(w => MentionsForm(haystack, w));
    }

    /// <summary>
    /// What a learner calls their subject, against the field it belongs to.
    /// </summary>
    /// <remarks>
    /// Only needed where the subject's own name shares no word with the adverts in it. Written
    /// as the learner would type it, matched on a prefix so "Civil Engineering", "civil
    /// engineering" and "Civil Eng" all land in the same place.
    /// </remarks>
    private static readonly (string Subject, string Field)[] Aliases =
    [
        ("civil engineer", "civil engineering"),
        ("mechanical engineer", "mechanical engineering"),
        ("electrical engineer", "electrical engineering"),
        ("chemical engineer", "chemical engineering"),
        ("architect", "architecture"),
        ("agricultur", "agriculture"),
        ("agronom", "agriculture"),
        ("mass communication", "mass communication"),
        ("hospitality", "hospitality"),
        ("fashion", "fashion"),
        ("human resource", "human resources"),
        ("taxation", "taxation"),
    ];

    private static string? AliasOf(string subject)
    {
        foreach (var (name, field) in Aliases)
        {
            if (subject.Contains(name, StringComparison.OrdinalIgnoreCase)) return field;
        }

        return null;
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

        foreach (var (ending, survivors) in Endings)
        {
            if (word.Length - ending.Length < survivors) continue;
            if (word.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) return word[..^ending.Length];
        }

        return word;
    }

    /// <summary>
    /// The endings a subject is written with, and how much has to survive trimming each one.
    /// </summary>
    /// <remarks>
    /// Longest first, so "ing" is tried before "s" would ever be.
    ///
    /// The floor is per ending rather than one number for all of them, which is what makes a
    /// bare "y" safe. It was excluded outright because it turns "policy" into "polic", which
    /// matches "police" - a public-policy learner shown Police Force recruitment. But "policy"
    /// is six letters and loses only one, while "psychology" loses one and keeps nine. Requiring
    /// six survivors admits the long ones and leaves "policy" alone.
    ///
    /// Measured against all fifty careers in the use-case corpus before being added: a bare "y"
    /// at six fixes Psychology, Physiotherapy, Library Science and Geology with no new failure
    /// anywhere, and "ion" at four fixes Public Administration without letting "Nutrition" reach
    /// "nutrient". "ation" was tried and rejected - it overshoots "Administration" to
    /// "Administr", which then cannot reach "Administrative" at all.
    ///
    /// Until this, a psychology graduate was not shown "Clinical psychologist, adolescent mental
    /// health service, Lagos". Her own field's jobs were invisible to her, which is a quieter
    /// failure than being shown somebody else's and a worse one.
    /// </remarks>
    private static readonly (string Suffix, int MinimumSurvivors)[] Endings =
    [
        ("ion", 4),
        ("ing", 4),
        ("ery", 4),
        ("ics", 6),
        ("es", 4),
        ("ed", 4),
        ("y", 6),
        ("s", 4),
    ];

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
        // The word itself, whatever it is. A learner whose subject genuinely is "nursery
        // education" matches "nursery" here, and the stem rule below never sees it.
        if (Bounded(text, word)) return true;

        var stem = Stem(word);
        if (string.Equals(stem, word, StringComparison.OrdinalIgnoreCase)) return false;

        return Bounded(text, stem, NotGrownFrom(stem));
    }

    /// <summary>
    /// Words a stem reaches that are a different subject entirely.
    /// </summary>
    /// <remarks>
    /// Stemming trades precision for recall deliberately - nursing/nurse, teaching/teacher - and
    /// three letters of growth is what makes those work. It is also exactly enough to get from
    /// "nurs" to "nursery", so a nursing student was shown "Nursery school expands its intake in
    /// Ikeja" as something in her field. Found by the use-case suite on its first run; every
    /// nursing case failed on it.
    ///
    /// Listed against the stem rather than banned outright, which is the part that matters. A
    /// learner whose subject is nursery education must still match "nursery" - it is their
    /// subject. What must not happen is "nurs" reaching it. The same shape as the ies/y fix
    /// above, where a bare "polic" matched "police" and a public-policy learner was shown Police
    /// Force recruitment.
    ///
    /// Short on purpose. A list that grows without a failing use case behind each entry is one
    /// nobody can justify later.
    /// </remarks>
    private static readonly Dictionary<string, string[]> FalseFriends = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nurs"] = ["nursery", "nurseries"],

        // Economics reaches "economist" and "economic", and one letter further to "economy" -
        // which is an aeroplane seat and a newspaper section before it is anybody's subject.
        ["econom"] = ["economy"],

        // Marketing stems to "market", which is a place people sell yams. A marketing graduate
        // was shown "Market women protest levy increases in Aba" as news in her field. Her own
        // advert says "Brand marketing executive", which matches the typed word before any
        // stemming happens, so nothing is lost.
        ["market"] = ["market", "markets"],
    };

    private static string[] NotGrownFrom(string stem) =>
        FalseFriends.TryGetValue(stem, out var words) ? words : [];

    /// <summary>
    /// Whether the text uses this word, allowing the endings a real sentence uses.
    /// </summary>
    /// <remarks>
    /// Every match is looked at rather than asking only whether one exists, so a sentence
    /// carrying both a false friend and a real use of the word still matches on the real one.
    /// </remarks>
    private static bool Bounded(string text, string word, string[]? notThese = null)
    {
        // A three-letter word may not grow at all.
        //
        // The budget was sized for nurse/nursing, and a three-letter word allowed to double is
        // a different word: "law" reached "lawn", so a law student was shown a lawn tennis
        // championship. Three-letter subjects only arrive here through the short-word escape
        // hatch, where they are the whole subject - and the comment there already promises
        // "law matches law and not lawn". This is the line that makes it true.
        var mayGrow = word.Length <= 3 ? 0 : MostAWordMayGrow;

        var matches = Regex.Matches(
            text,
            $@"\b{Regex.Escape(word)}[a-z]{{0,{mayGrow}}}\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        foreach (Match match in matches)
        {
            if (notThese is null || notThese.Length == 0) return true;
            if (!notThese.Contains(match.Value, StringComparer.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

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

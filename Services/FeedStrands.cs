using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// The only three things Radar Intelligence shows, and how an item qualifies for one.
/// </summary>
/// <remarks>
/// The feed used to be one query: map the reader's interests onto content layers, keep items in
/// those layers, and — if no layer matched — keep <b>everything</b>. Both halves of that were
/// wrong in the same direction.
///
/// The layers are enormous. "Artificial Intelligence" maps to Ideas, Science and Skills, which
/// between them hold most of the corpus, so the filter was close to a no-op for anybody whose
/// interests were broad. And the fallback did not narrow anything at all: a reader whose
/// interests mapped to no layer got the entire library under a heading promising relevance.
///
/// So a feed item never had to be about anything the reader had said. That is the same shape as
/// the lesson that offered an IPO report to a maths student, and it is fixed the same way: an
/// item has to earn its place by matching something the reader actually wrote, and an item that
/// matches nothing is not shown.
///
/// Three strands, and nothing outside them:
///
/// <list type="number">
/// <item><b>Interest</b> — developments in the subjects they follow.</item>
/// <item><b>Goal</b> — material that serves what they are working towards or what is in their
/// way, weighted by whether they are a student, a graduate or a professional.</item>
/// <item><b>Opportunity</b> — things they can actually apply for. A different shape entirely, so
/// it is selected elsewhere; it is named here because the feed is defined as these three and a
/// list of three that only enumerates two invites a fourth.</item>
/// </list>
/// </remarks>
public static class FeedStrands
{
    /// <summary>Why an item is in the feed. Shown, so a reader can disagree with it.</summary>
    public enum Strand
    {
        /// <summary>It matched nothing. Never shown — present so the default is not a lie.</summary>
        None = 0,
        Interest,
        Goal,
        Opportunity,
    }

    /// <summary>
    /// How many distinct terms to carry into a query.
    /// </summary>
    /// <remarks>
    /// A reader may list twenty interests. Twenty alternations against an unindexed field is a
    /// collection scan that grows with how much somebody told us, which punishes exactly the
    /// readers who told us most.
    /// </remarks>
    public const int MaxTerms = 12;

    /// <summary>
    /// The words from a profile worth matching content against.
    /// </summary>
    /// <remarks>
    /// Taken from what the reader wrote, not from a catalogue. An interest Radar has never heard
    /// of still selects content, which the layer mapping could not do — it returned nothing for
    /// any subject outside its keyword list, so a reader who typed their own got no layer hint at
    /// all and fell straight through to the catch-all.
    ///
    /// Goals and challenges are pooled. They are different questions but they want the same
    /// thing: material about getting from here to there. Keeping them apart would mean two
    /// near-identical queries and a distinction no reader asked for.
    /// </remarks>
    public static List<string> TermsFor(Strand strand, UserProfile profile)
    {
        return strand switch
        {
            Strand.Interest => Bounded([profile.Interests]),

            // Goals and challenges alternate rather than being concatenated. Appending one to the
            // other and taking the first twelve meant a reader who answered both boxes generously
            // got twelve terms from their goals and every challenge discarded — box 3 being the
            // newest box and the first thrown away, silently.
            Strand.Goal => Bounded([profile.Goals, profile.Problems]),

            _ => [],
        };
    }

    /// <summary>
    /// Terms from several answers, taking from each in turn so no answer is dropped wholesale.
    /// </summary>
    private static List<string> Bounded(IReadOnlyList<List<string>> sources)
    {
        var queues = sources
            .Select(source => source.SelectMany(Phrase.MeaningfulWords).ToList())
            .ToList();

        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var depth = 0; terms.Count < MaxTerms; depth++)
        {
            var exhausted = true;

            foreach (var queue in queues)
            {
                if (depth >= queue.Count) continue;

                exhausted = false;
                if (seen.Add(queue[depth])) terms.Add(queue[depth]);

                if (terms.Count == MaxTerms) return terms;
            }

            if (exhausted) break;
        }

        return terms;
    }

    /// <summary>
    /// Matches an item that mentions any of these terms where it counts.
    /// </summary>
    /// <remarks>
    /// Topic and tags first, because those are what the enrichment decided the item is
    /// <i>about</i>; the title is included because enrichment is not always right and a headline
    /// naming the subject is evidence too.
    ///
    /// Word starts, not substrings. "AI" must not match "said", and "art" must not match "smart"
    /// — the mistake that put a tourism paper in a lesson on model output.
    /// </remarks>
    public static FilterDefinition<ContentItem> Mentioning(IReadOnlyCollection<string> terms)
    {
        if (terms.Count == 0) return Builders<ContentItem>.Filter.Where(_ => false);

        var clauses = new List<FilterDefinition<ContentItem>>();

        foreach (var term in terms)
        {
            var pattern = new BsonRegularExpression(LessonRelevance.WordStartPattern(term), "i");

            clauses.Add(Builders<ContentItem>.Filter.Regex(i => i.Topic, pattern));
            clauses.Add(Builders<ContentItem>.Filter.Regex(i => i.Title, pattern));

            // A regex against an array field, which Mongo applies to each element. AnyEq would
            // compare the tags to the literal text of the pattern and never match anything.
            clauses.Add(Builders<ContentItem>.Filter.Regex("Tags", pattern));
        }

        return Builders<ContentItem>.Filter.Or(clauses);
    }

    /// <summary>
    /// Which strand an item belongs to, given what matched it.
    /// </summary>
    /// <remarks>
    /// Interest wins ties. An item that is both about a subject they follow and useful for a goal
    /// is more simply explained as the former, and the card has one line to explain itself in.
    /// </remarks>
    public static Strand StrandOf(
        ContentItem item, IReadOnlyCollection<string> interestTerms, IReadOnlyCollection<string> goalTerms)
    {
        var text = $"{item.Topic} {item.Title} {string.Join(" ", item.Tags)}";

        if (LessonRelevance.CountTerms(text, interestTerms) > 0) return Strand.Interest;
        if (LessonRelevance.CountTerms(text, goalTerms) > 0) return Strand.Goal;

        return Strand.None;
    }
}

/// <summary>Pulling the words that carry meaning out of something a reader typed.</summary>
public static class Phrase
{
    /// <summary>
    /// Words that appear in what people write about goals and obstacles without saying anything
    /// about the subject.
    /// </summary>
    /// <remarks>
    /// Short, and not load-bearing. Goals and challenges are written as sentences — "I keep
    /// getting rejected", "Find a job" — so without this the feed would be selected on "keep" and
    /// "getting". Anything that slips through still has to survive the ranking.
    /// </remarks>
    private static readonly HashSet<string> Scaffolding = new(StringComparer.OrdinalIgnoreCase)
    {
        "want", "need", "keep", "getting", "have", "having", "this", "that", "with", "from",
        "into", "about", "my", "me", "i", "a", "an", "the", "and", "or", "for", "to", "of",
        "in", "on", "at", "by", "is", "it", "as", "be", "do", "we", "he", "so", "up", "us",
        "am", "are", "was", "but", "out", "off", "too", "own", "yet", "per", "via",
        "find", "get", "make", "take", "look", "looking", "trying", "cannot", "can't", "don't",
        "not", "no", "any", "more", "most", "very", "really", "just", "still", "right", "wrong",
        "good", "better", "best", "new", "own", "enough", "sure", "hard", "difficult", "feel",
        "feels", "start", "started", "finish", "know", "how", "what", "where", "when", "why",
        "improve", "advance", "build", "grow", "learn", "study", "prepare", "change",
    };

    /// <summary>The words in a phrase that are worth matching content on.</summary>
    public static IEnumerable<string> MeaningfulWords(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return [];

        // Apostrophes are removed, not split on.
        //
        // Splitting turned "don't" into "don" and "can't" into "can" — and seven of the eighteen
        // suggested challenges contain "don't". On an Africa-focused corpus "don" matches Donald,
        // donor and donate, and "can" matches Canada, cancer, candidates and cannabis, so a reader
        // who wrote "I don't know where to start" would be shown Nigerian political news.
        //
        // The same class of mistake as "art" matching "smart", which the word-start rule exists to
        // prevent — arrived at from the other end.
        return phrase
            .Replace("'", string.Empty)
            .Replace("’", string.Empty)
            .Split([' ', ',', ';', ':', '-', '—', '/', '(', ')', '.', '"', '?', '!'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            // Two letters is a subject here: AI, ML, UX, HR, VC. Requiring three dropped exactly
            // the interests most of Radar's readers list first. The short words that carry no
            // meaning are named below instead of being excluded by length.
            .Where(w => w.Length > 1)
            .Where(w => !Scaffolding.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

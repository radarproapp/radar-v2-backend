using System.Text.RegularExpressions;

namespace RadarV2.Services;

/// <summary>
/// Deciding whether something belongs in a lesson.
/// </summary>
/// <remarks>
/// A lesson called "Understanding Numbers", in Foundations of Mathematics, offered two readings:
/// "Anthropic IPO numbers surprise on the upside" and "Ozempic is changing how Americans spend
/// money, and the numbers are startling". Both arrived on the single word <c>numbers</c>.
///
/// Three things were wrong and all three are fixed here.
///
/// <list type="number">
/// <item><b>Substring, not word.</b> The match was a bare regex, so <c>model</c> found
/// <c>modeling</c> and <c>art</c> would find <c>smart</c> — which is how a tourism paper reached a
/// lesson on model output. Terms now anchor to a word boundary at the start, which still catches
/// plurals and <c>-ing</c> forms without matching the inside of an unrelated word.</item>
///
/// <item><b>Any word, not a meaningful one.</b> Matching was an OR, so one weak term carried an
/// item in on its own, which is exactly what <c>numbers</c> did twice. A candidate now has to
/// clear <see cref="MinimumTerms"/> distinct terms, or match a single term the corpus says is rare
/// enough to mean something by itself.</item>
///
/// <item><b>A hand-written list of worthless words.</b> The old filler list said of itself that it
/// "is not exhaustive and cannot be", and it was right: a word was only ever added after it had
/// already put the wrong thing in front of somebody. Which words carry no signal is not a matter
/// of opinion and does not need a list — it is how much of the corpus they match. A term matching
/// a large share of everything Radar holds cannot distinguish anything within it, whoever writes
/// the list.</item>
/// </list>
///
/// None of this decides what a learner sees. It decides what is worth asking the AI engine about,
/// which is the gate that does. The job here is to be cheap, explainable, and wrong in the safe
/// direction.
/// </remarks>
public static class LessonRelevance
{
    /// <summary>
    /// How much of the corpus a term may match and still mean something on its own.
    /// </summary>
    /// <remarks>
    /// A term matching more than this is not evidence by itself. It is not noise either — two of
    /// them together still say something — so it stays available for <see cref="MinimumTerms"/>
    /// rather than being discarded.
    ///
    /// Two in a thousand. It was twenty, which was far too loose to do anything: a single word
    /// reaching two per cent of all titles in a general corpus is close to unheard of, so nearly
    /// every term counted as rare — including "numbers", the term that caused the reported bug.
    /// The rule was written down but not in force.
    ///
    /// Worth being plain about the limit, because a number cannot fix this on its own. A lesson
    /// titled "Understanding Numbers" has exactly one content word, so there is no second term to
    /// require and no threshold that separates a maths lesson from an IPO headline on the word
    /// they genuinely share. For titles like that the judgement is the only real filter, and this
    /// stage only decides what it is worth asking about.
    /// </remarks>
    public const double CommonTermShare = 0.002;

    /// <summary>
    /// How many distinct lesson terms an item must match when none of them is rare.
    /// </summary>
    /// <remarks>
    /// Two, because one was the bug. Two common terms co-occurring is weak evidence, but it is
    /// evidence; one is a coincidence, and at corpus scale a coincidence is a certainty.
    /// </remarks>
    public const int MinimumTerms = 2;

    /// <summary>
    /// Words a lesson title has because it is a lesson title.
    /// </summary>
    /// <remarks>
    /// Kept, and it still has ordinary English in it — but it is no longer load-bearing, and that
    /// is the difference that matters.
    ///
    /// The old list was the only thing standing between a lesson title and the corpus, so it had
    /// to name every word that could ever mislead, and it admitted it could not. This one runs in
    /// front of two gates that do not need it to be complete: a term that slips through is still
    /// measured against the corpus, and anything that survives that is still judged. So it can be
    /// a short list of words that are obviously about teaching rather than about a subject, and
    /// being incomplete costs a count query rather than a wrong reading.
    ///
    /// A title of nothing but these yields no terms at all, which means no query runs — cheaper
    /// than discovering the same thing twice downstream.
    /// </remarks>
    private static readonly HashSet<string> LessonScaffolding = new(StringComparer.OrdinalIgnoreCase)
    {
        "introduction", "intro", "basics", "overview", "fundamentals", "getting", "started",
        "learn", "learning", "lesson", "module", "course", "beginner", "beginners", "advanced",
        "understand", "understanding", "guide", "explained", "part", "what", "why", "how",
        "really", "actually", "need", "needs", "want", "wants", "your", "yours", "this", "that",
    };

    /// <summary>
    /// The terms a lesson can be matched on.
    /// </summary>
    /// <remarks>
    /// The search hint is included when there is one. It exists because a readable title makes a
    /// poor query, and the same thing that makes it a better query makes it a better match: it
    /// names the subject rather than describing the lesson.
    ///
    /// Not truncated to three any more. Taking the first three words of a title discarded the
    /// specific terms in favour of whatever came first, and the specific ones are the point.
    /// </remarks>
    public static List<string> TermsOf(string title, string? searchHint = null)
    {
        var source = string.IsNullOrWhiteSpace(searchHint) ? title : title + " " + searchHint;

        return source
            .Split([' ', ',', ':', ';', '-', '—', '(', ')', '/', '.', '?', '!', '"'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 3)
            .Where(w => !LessonScaffolding.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(w => w.Length)
            .Take(8)
            .ToList();
    }

    /// <summary>
    /// A pattern matching a term at the start of a word.
    /// </summary>
    /// <remarks>
    /// Anchored at the front only, on purpose. It matches "prompts" and "prompting" for
    /// <c>prompt</c>, which is what a reader means. What it will not do is find <c>model</c>
    /// inside <c>remodel</c> or <c>art</c> inside <c>smart</c>.
    /// </remarks>
    public static string WordStartPattern(string term) => @"\b" + Regex.Escape(term);

    /// <summary>How many of these terms begin a word in the text.</summary>
    public static int CountTerms(string? text, IReadOnlyCollection<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0) return 0;

        return terms.Count(term => Regex.IsMatch(
            text, WordStartPattern(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    /// <summary>
    /// Whether a term is rare enough in the corpus to stand on its own.
    /// </summary>
    /// <remarks>
    /// Rare, not absent. A term matching no title at all scored zero, which passed the threshold
    /// and made it the strongest possible evidence — so a word that appears nowhere in the corpus
    /// could single-handedly admit an item it had matched only in a summary, which is exactly the
    /// two-term rule being bypassed.
    /// </remarks>
    public static bool IsDistinctive(long matchingItems, long corpusSize) =>
        corpusSize > 0
        && matchingItems > 0
        && (double)matchingItems / corpusSize <= CommonTermShare;

    /// <summary>
    /// Whether an item is worth asking the AI engine about.
    /// </summary>
    /// <remarks>
    /// Deliberately permissive: this is the gate before the judgement, not instead of it. Letting
    /// a borderline item through costs a fraction of one batched call; rejecting a good one here
    /// means the learner never sees it and nothing records that a filter made that choice.
    /// </remarks>
    public static bool WorthJudging(
        string? candidateText,
        IReadOnlyCollection<string> terms,
        IReadOnlyCollection<string> distinctiveTerms)
    {
        if (string.IsNullOrWhiteSpace(candidateText)) return false;

        // One rare term is enough. "prompt" appearing at all says more than "model" and
        // "training" appearing together.
        if (distinctiveTerms.Count > 0 && CountTerms(candidateText, distinctiveTerms) > 0) return true;

        return CountTerms(candidateText, terms) >= MinimumTerms;
    }

    /// <summary>
    /// Candidates in the order they should be offered for judgement.
    /// </summary>
    /// <remarks>
    /// The implementation guide fixes this order in section 20: relevance, then accuracy and
    /// source credibility, then recency. The old sort had credibility first and recency second,
    /// with relevance absent from it altogether — so among everything a keyword dragged in, the
    /// most reputable and most recent won, whatever it was about. That is how a lesson on
    /// arithmetic ends up offering a well-sourced, freshly published article about an IPO.
    ///
    /// Relevance first also makes the ordering do real work: this list is cut to
    /// <see cref="LessonResourceJudge.MaxCandidates"/> before judging, so what sorts to the top is
    /// what gets considered at all.
    ///
    /// Recency is last of the three on purpose. A learner is not reading the news, and a good
    /// explanation of a stable subject does not expire.
    /// </remarks>
    public static IEnumerable<T> Rank<T>(
        IEnumerable<T> candidates,
        Func<T, string?> textOf,
        Func<T, int> credibilityTierOf,
        Func<T, DateTime?> publishedAtOf,
        IReadOnlyCollection<string> terms,
        IReadOnlyCollection<string> distinctiveTerms) =>
        candidates
            .OrderByDescending(c => CountTerms(textOf(c), distinctiveTerms))
            .ThenByDescending(c => CountTerms(textOf(c), terms))
            // Tier 1 is the best tier, so ascending puts the most credible first.
            .ThenBy(credibilityTierOf)
            .ThenByDescending(c => publishedAtOf(c) ?? DateTime.MinValue);

    /// <summary>
    /// Removes items that are the same thing twice.
    /// </summary>
    /// <remarks>
    /// Section 20 again: where several sources carry the same development, do not show the
    /// duplicates. The corpus has literal ones — searching it returns "Performance Analysis of
    /// Solar-Powered Cooling Systems." twice — and a two-item reading list that is one item
    /// repeated is a reading list with nothing in it.
    ///
    /// Matched on the destination and on the title, because both kinds occur: the same URL
    /// ingested twice, and the same piece republished at two URLs. Titles are compared with
    /// punctuation and spacing removed, since that is the usual difference between them.
    /// </remarks>
    public static IEnumerable<T> Distinct<T>(
        IEnumerable<T> candidates, Func<T, string?> urlOf, Func<T, string?> titleOf)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var url = urlOf(candidate)?.Trim();
            if (!string.IsNullOrWhiteSpace(url) && !seenUrls.Add(url)) continue;

            var title = Flatten(titleOf(candidate));
            if (!string.IsNullOrWhiteSpace(title) && !seenTitles.Add(title)) continue;

            yield return candidate;
        }
    }

    /// <summary>A title reduced to its letters and digits, for comparing two of them.</summary>
    private static string Flatten(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : new string(title.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

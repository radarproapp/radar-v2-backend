using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The readings offered for a lesson have to be about the lesson.
///
/// Reported from production: a lesson called "Understanding Numbers", in Foundations of
/// Mathematics, offered "Anthropic IPO numbers surprise on the upside" and "Ozempic is changing
/// how Americans spend money, and the numbers are startling". Both arrived on one shared word.
///
/// Every case below is a real pairing from the corpus rather than an invented one, because the
/// thing that made this survive was that each individual match looked defensible.
/// </summary>
public class LessonRelevanceTests
{
    /// <summary>
    /// The reported pair, as the first gate sees it.
    /// </summary>
    /// <remarks>
    /// "Numbers" is the only term "Understanding Numbers" has — "Understanding" is scaffolding.
    /// So the whole question is whether one shared common word is enough, and it must not be.
    /// </remarks>
    [Theory]
    [InlineData("Anthropic IPO numbers surprise on the upside")]
    [InlineData("Ozempic is changing how Americans spend money, and the numbers are startling")]
    public void OneSharedCommonWordIsNotEnough(string articleTitle)
    {
        var terms = LessonRelevance.TermsOf("Understanding Numbers");

        // Derived, not assumed. An earlier version of this test passed [] for the distinctive
        // terms and so asserted nothing about the rule it was named for — it handed the function
        // the answer. "numbers" appearing in 40 of 10,000 titles is a realistic frequency for a
        // common word, and the threshold has to reject it for this assertion to mean anything.
        var distinctive = LessonRelevance.IsDistinctive(matchingItems: 40, corpusSize: 10_000)
            ? terms
            : [];

        Assert.Empty(distinctive);
        Assert.False(LessonRelevance.WorthJudging(articleTitle, terms, distinctive));
    }

    /// <summary>
    /// The threshold has to sit where ordinary words fall outside it.
    /// </summary>
    /// <remarks>
    /// It was two per cent, and nothing pinned it. A single word reaching two per cent of every
    /// title in a general corpus is close to unheard of, so effectively every term counted as rare
    /// and the "one rare term is enough" path admitted almost anything — including the exact pair
    /// reported from production. The rule was written down but not in force.
    ///
    /// These two cases bracket it tightly enough that moving it in either direction fails one.
    /// </remarks>
    [Fact]
    public void TheThresholdRejectsAnOrdinaryWordAndAcceptsARareOne()
    {
        // A common word: 0.4% of titles.
        Assert.False(LessonRelevance.IsDistinctive(matchingItems: 40, corpusSize: 10_000));

        // A genuine term of art: 0.1%.
        Assert.True(LessonRelevance.IsDistinctive(matchingItems: 10, corpusSize: 10_000));
    }

    /// <summary>
    /// A term in no title at all is not evidence, it is absence.
    /// </summary>
    /// <remarks>
    /// Zero matches scored 0/N, which passed the threshold and made the term maximally
    /// distinctive — so a word appearing nowhere in the corpus could single-handedly admit an item
    /// it had matched only in a summary, bypassing the two-term rule entirely.
    /// </remarks>
    [Fact]
    public void ATermThatMatchesNothingIsNotDistinctive()
    {
        Assert.False(LessonRelevance.IsDistinctive(matchingItems: 0, corpusSize: 10_000));
    }

    /// <summary>
    /// And the substring half of it: "model" must not find "modeling".
    /// </summary>
    /// <remarks>
    /// The old matcher was a bare escaped regex, so any term was matched anywhere inside any word.
    /// That is how a lesson on model output reached "MODELING HOSPITALITY AND TOURISM STRATEGIES",
    /// and how "art" would have reached anything "smart".
    /// </remarks>
    [Fact]
    public void ATermDoesNotMatchTheInsideOfAnotherWord()
    {
        Assert.Equal(0, LessonRelevance.CountTerms("A remodelled kitchen", ["model"]));
        Assert.Equal(0, LessonRelevance.CountTerms("Smart cities in Lagos", ["art"]));
    }

    /// <summary>
    /// But it does match the start of one, so plurals and -ing forms still count.
    /// </summary>
    /// <remarks>
    /// Anchoring both ends would be stricter and wrong: a reader searching for "prompt" means
    /// "prompts" and "prompting" too, and a gate that misses those finds nothing at all.
    /// </remarks>
    [Fact]
    public void ATermMatchesTheStartOfAWord()
    {
        Assert.Equal(1, LessonRelevance.CountTerms("Writing prompts that work", ["prompt"]));
        Assert.Equal(1, LessonRelevance.CountTerms("Prompting techniques", ["prompt"]));
    }

    [Fact]
    public void TwoSharedTermsAreEnoughToBeWorthAsking()
    {
        var terms = LessonRelevance.TermsOf("Designing prompts for consistent model output");

        Assert.True(LessonRelevance.WorthJudging(
            "A practical guide to prompt design for model output", terms, []));
    }

    /// <summary>One rare term stands on its own, where a common one cannot.</summary>
    [Fact]
    public void ASingleDistinctiveTermIsEnough()
    {
        var terms = LessonRelevance.TermsOf("Designing prompts for consistent model output");

        Assert.True(LessonRelevance.WorthJudging(
            "What prompts actually do", terms, ["prompts"]));
    }

    /// <summary>An empty corpus makes nothing distinctive, rather than everything.</summary>
    [Fact]
    public void NothingIsDistinctiveInAnEmptyCorpus()
    {
        Assert.False(LessonRelevance.IsDistinctive(matchingItems: 0, corpusSize: 0));
    }

    /// <summary>
    /// A title of nothing but teaching words produces no terms, so no query runs.
    /// </summary>
    [Fact]
    public void ATitleWithNoSubjectInItYieldsNothingToSearchOn()
    {
        Assert.Empty(LessonRelevance.TermsOf("What you really need to understand"));
    }

    /// <summary>
    /// Relevance leads the ranking, as section 20 requires.
    /// </summary>
    /// <remarks>
    /// The old sort was credibility then recency, with relevance absent from it — so among
    /// everything a keyword dragged in, the most reputable and most recent won, whatever it was
    /// about. A well-sourced article published today that merely shares a word must still lose to
    /// a less prominent one that is actually on the subject.
    /// </remarks>
    [Fact]
    public void RelevanceOutranksCredibilityAndRecency()
    {
        var terms = LessonRelevance.TermsOf("Designing prompts for consistent model output");

        var candidates = new[]
        {
            new Candidate("Model railway output hits record", Tier: 1, DateTime.UtcNow),
            new Candidate("Prompt design for model output", Tier: 4, DateTime.UtcNow.AddYears(-3)),
        };

        var ranked = LessonRelevance.Rank(
            candidates, c => c.Title, c => c.Tier, c => c.Published, terms, ["prompt"]).ToList();

        Assert.Equal("Prompt design for model output", ranked[0].Title);
    }

    /// <summary>
    /// The same thing twice is one thing.
    /// </summary>
    /// <remarks>
    /// Section 20 asks for this, and the corpus needs it: searching it returns "Performance
    /// Analysis of Solar-Powered Cooling Systems." twice. A two-item reading list that is one item
    /// repeated has nothing in it.
    /// </remarks>
    [Fact]
    public void TheSameItemIsNotOfferedTwice()
    {
        var candidates = new[]
        {
            new Candidate("Performance Analysis of Solar-Powered Cooling Systems.", 1, null, "a"),
            new Candidate("Performance analysis of solar-powered cooling systems", 1, null, "b"),
            new Candidate("Something else entirely", 1, null, "c"),
        };

        var kept = LessonRelevance.Distinct(candidates, c => c.Url, c => c.Title).ToList();

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void TheSameUrlIsNotOfferedTwice()
    {
        var candidates = new[]
        {
            new Candidate("One headline", 1, null, "https://example.com/a"),
            new Candidate("A different headline for the same page", 1, null, "https://example.com/a"),
        };

        Assert.Single(LessonRelevance.Distinct(candidates, c => c.Url, c => c.Title));
    }

    private sealed record Candidate(
        string Title, int Tier, DateTime? Published, string? Url = null);
}

using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Radar Intelligence shows three things, and an item has to earn its place in one of them.
///
/// It used to show whatever was in a content layer the reader's interests mapped to — and the
/// whole corpus when nothing mapped. The layers are enormous, so even the working path barely
/// narrowed anything: a feed item never had to be about anything the reader had said.
/// </summary>
public class FeedStrandTests
{
    private static UserProfile Profile(
        string[]? interests = null, string[]? goals = null, string[]? problems = null) => new()
    {
        Id = "p1",
        Interests = [.. interests ?? []],
        Goals = [.. goals ?? []],
        Problems = [.. problems ?? []],
    };

    // ── What a profile offers to match on ───────────────────────────────────

    /// <summary>
    /// Two letters is a subject. AI, ML, UX, HR — requiring three dropped exactly the interests
    /// Radar's readers list first.
    /// </summary>
    [Theory]
    [InlineData("AI")]
    [InlineData("ML")]
    [InlineData("UX")]
    public void AShortSubjectIsStillASubject(string interest)
    {
        Assert.Contains(
            interest,
            FeedStrands.TermsFor(FeedStrands.Strand.Interest, Profile(interests: [interest])),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Goals and challenges are written as sentences, so the words holding them together have to
    /// come out — otherwise the feed is selected on "keep" and "getting".
    /// </summary>
    [Fact]
    public void TheWordsHoldingASentenceTogetherAreNotMatchedOn()
    {
        var terms = FeedStrands.TermsFor(
            FeedStrands.Strand.Goal, Profile(problems: ["I keep getting rejected"]));

        Assert.Contains("rejected", terms, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("keep", terms, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("getting", terms, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Goals and challenges are pooled: they are different questions that want the same material.
    /// </summary>
    [Fact]
    public void GoalsAndChallengesAreMatchedTogether()
    {
        var terms = FeedStrands.TermsFor(
            FeedStrands.Strand.Goal,
            Profile(goals: ["Become a data analyst"], problems: ["I have no portfolio"]));

        Assert.Contains("analyst", terms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("portfolio", terms, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A reader who lists twenty interests must not cost twenty alternations on every page.
    /// </summary>
    [Fact]
    public void TheTermListIsBounded()
    {
        var many = Enumerable.Range(0, 40).Select(i => $"Subject{i}").ToArray();

        Assert.Equal(
            FeedStrands.MaxTerms,
            FeedStrands.TermsFor(FeedStrands.Strand.Interest, Profile(interests: many)).Count);
    }

    // ── Which strand an item is in ──────────────────────────────────────────

    private static ContentItem Item(string title, string topic = "", params string[] tags) =>
        new() { Title = title, Topic = topic, Tags = [.. tags] };

    [Fact]
    public void AnItemAboutASubjectTheyFollowIsAnInterestItem()
    {
        var strand = FeedStrands.StrandOf(
            Item("New transformer architecture published", topic: "Machine Learning"),
            ["Machine Learning"], ["portfolio"]);

        Assert.Equal(FeedStrands.Strand.Interest, strand);
    }

    [Fact]
    public void AnItemThatServesAGoalIsAGoalItem()
    {
        var strand = FeedStrands.StrandOf(
            Item("How to build a data portfolio that gets interviews"),
            ["Machine Learning"], ["portfolio"]);

        Assert.Equal(FeedStrands.Strand.Goal, strand);
    }

    /// <summary>
    /// The one that matters: an item matching nothing the reader wrote is in no strand, and the
    /// feed is defined as the strands.
    /// </summary>
    [Fact]
    public void AnItemMatchingNothingIsInNoStrand()
    {
        var strand = FeedStrands.StrandOf(
            Item("Ozempic is changing how Americans spend money", topic: "Health"),
            ["Machine Learning"], ["portfolio"]);

        Assert.Equal(FeedStrands.Strand.None, strand);
    }

    /// <summary>
    /// Interest wins when both match, because the card has one line to explain itself in and
    /// "this is about a subject you follow" is the simpler account.
    /// </summary>
    [Fact]
    public void AnItemThatIsBothIsExplainedAsAnInterest()
    {
        var strand = FeedStrands.StrandOf(
            Item("Building a machine learning portfolio"),
            ["Machine Learning"], ["portfolio"]);

        Assert.Equal(FeedStrands.Strand.Interest, strand);
    }

    // ── The query it builds ─────────────────────────────────────────────────

    /// <summary>
    /// Tags are matched with a regex, which Mongo applies to each element of the array.
    /// </summary>
    /// <remarks>
    /// This was written as AnyEq against the pattern's text, which compares every tag to the
    /// literal string "/\bAI/i" and matches nothing — a filter that looked right, built cleanly,
    /// and silently contributed no results.
    /// </remarks>
    [Fact]
    public void TagsAreMatchedByPatternRatherThanByTheTextOfThePattern()
    {
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = FeedStrands.Mentioning(["prompt"])
            .Render(new RenderArgs<ContentItem>(registry.GetSerializer<ContentItem>(), registry))
            .ToString();

        // Tags is matched by a pattern applied to each element, not by comparing the tag to the
        // text of the pattern - which is what AnyEq did, and which matches nothing.
        Assert.Contains("Tags", rendered);
        Assert.Contains("$regularExpression", rendered);
        Assert.DoesNotContain("\"Tags\" : \"", rendered);
    }

    /// <summary>
    /// No terms must mean no items, not every item. The old fallback returned an untouched filter
    /// when nothing matched, which is the whole corpus.
    /// </summary>
    [Fact]
    public void NothingToMatchOnMatchesNothing()
    {
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = FeedStrands.Mentioning([])
            .Render(new RenderArgs<ContentItem>(registry.GetSerializer<ContentItem>(), registry))
            .ToString();

        Assert.NotEqual("{ }", rendered);
    }

    // ── What the review caught ──────────────────────────────────────────────

    /// <summary>
    /// A contraction must not leak a broad word into the query.
    /// </summary>
    /// <remarks>
    /// Splitting on the apostrophe turned "don't" into "don" and "can't" into "can" — and seven
    /// of the eighteen suggested challenges contain "don't". On an Africa-focused corpus "don"
    /// matches Donald, donor and donate; "can" matches Canada, cancer and candidates. A reader
    /// who wrote that they did not know where to start was shown political news.
    /// </remarks>
    [Theory]
    [InlineData("I don't know where to start", "don")]
    [InlineData("I can't afford the course", "can")]
    public void AContractionDoesNotLeakABroadTerm(string challenge, string leaked)
    {
        var terms = FeedStrands.TermsFor(FeedStrands.Strand.Goal, Profile(problems: [challenge]));

        Assert.DoesNotContain(leaked, terms, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Box 3 was the newest box and the first thrown away.
    /// </summary>
    /// <remarks>
    /// The terms were goals and challenges concatenated, then truncated — so a reader who
    /// answered both boxes generously got twelve terms from their goals and every challenge
    /// discarded, silently.
    /// </remarks>
    [Fact]
    public void ChallengesSurviveAReaderWithManyGoals()
    {
        var manyGoals = Enumerable.Range(0, 20).Select(i => $"Goalword{i}").ToArray();

        var terms = FeedStrands.TermsFor(
            FeedStrands.Strand.Goal, Profile(goals: manyGoals, problems: ["portfolio"]));

        Assert.Contains("portfolio", terms, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Personalised is not the same as having something to match on.
    /// </summary>
    /// <remarks>
    /// IsPersonalised only wants one non-blank entry per box, not one that yields a term. Both
    /// boxes can be answered with scaffolding — interests "New", goals "Learn more" — and that
    /// profile used to receive the entire corpus, behind a comment asserting it could not happen.
    /// </remarks>
    [Fact]
    public void AProfileOfNothingButFillerYieldsNoTerms()
    {
        var profile = Profile(interests: ["New"], goals: ["Learn more"]);

        Assert.Empty(FeedStrands.TermsFor(FeedStrands.Strand.Interest, profile));
        Assert.Empty(FeedStrands.TermsFor(FeedStrands.Strand.Goal, profile));
        Assert.True(profile.IsPersonalised);
    }

    // ── Reaching the words the corpus actually uses ─────────────────────────

    private static string Render(MongoDB.Driver.FilterDefinition<ContentItem> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter
            .Render(new RenderArgs<ContentItem>(registry.GetSerializer<ContentItem>(), registry))
            .ToString();
    }

    /// <summary>
    /// The production path searches for a short subject, not only the term extractor does.
    /// </summary>
    /// <remarks>
    /// This is the test that was missing, and the gap is worth recording. There was already a
    /// test asserting AI, ML and UX survive - but it called TermsFor, and the feed calls
    /// AboutSubjects. When AboutSubjects was changed to use a word list with a four-character
    /// floor, "Law" began contributing no clauses at all and "AI" stopped searching for itself,
    /// while that test went on passing. A test that does not run the path the product runs is
    /// evidence about nothing.
    /// </remarks>
    [Theory]
    [InlineData("AI")]
    [InlineData("Law")]
    [InlineData("UX")]
    [InlineData("HR")]
    public void AShortSubjectIsSearchedForOnThePathTheFeedUses(string interest)
    {
        var rendered = Render(FeedStrands.AboutSubjects([interest]));

        Assert.Contains(interest, rendered, StringComparison.Ordinal);
        Assert.Contains("regularExpression", rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A subject is searched for as the reader wrote it, not reduced to a fragment.
    /// </summary>
    /// <remarks>
    /// Stemming was tried and reverted. As word-start prefixes the stems were far too broad:
    /// "Cultural Studies" became "Stud" and matched student, students, study and studio;
    /// "Banking" became "Bank" and matched bankruptcy; "Nursing" became "Nurs" and matched
    /// nursery. Selection is the only control - the page is chosen by recency and then ranked, so
    /// ranking cannot demote a loose match off a page it has already been admitted to.
    /// </remarks>
    [Fact]
    public void ASubjectIsNotReducedToAFragment()
    {
        var rendered = Render(FeedStrands.AboutSubjects(["Cultural Studies"]));

        Assert.Contains("Studies", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\bStud""", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// A field's synonyms are not pulled in either, for a quieter reason.
    /// </summary>
    /// <remarks>
    /// The field is chosen by first keyword match and "engineer" belongs to the software field,
    /// so civil, mechanical, electrical, chemical, aerospace and biomedical engineering all
    /// acquired "software", "developer" and "backend". Widening this needs whole-word matching
    /// and a field lookup that does not resolve nine disciplines to one.
    /// </remarks>
    [Fact]
    public void ADisciplineDoesNotAcquireAnotherFieldsVocabulary()
    {
        var rendered = Render(FeedStrands.AboutSubjects(["Civil Engineering"]));

        Assert.DoesNotContain("software", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backend", rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The body is searched only for the whole subject.
    /// </summary>
    /// <remarks>
    /// A single word loose in a body is how a feed fills with near-misses: "product" appears in
    /// half the business coverage ever written, while "product management" appearing in a summary
    /// is the item being about it.
    /// </remarks>
    [Fact]
    public void TheBodyIsSearchedForAPhraseAndNotForOneWord()
    {
        Assert.Contains("WhatHappened", Render(FeedStrands.AboutSubjects(["Product Management"])));
        Assert.DoesNotContain("WhatHappened", Render(FeedStrands.AboutSubjects(["Robotics"])));
    }

    /// <summary>
    /// A subject Radar has never heard of still searches for itself.
    /// </summary>
    /// <remarks>
    /// The layer mapping returned nothing for anything outside its keyword list, so a reader who
    /// typed their own subject got no hint at all. Nothing here privileges a listed field.
    /// </remarks>
    [Fact]
    public void AnUnlistedSubjectStillSearches()
    {
        Assert.Contains("Ethnomusicology", Render(FeedStrands.AboutSubjects(["Ethnomusicology"])));
    }

    /// <summary>A subject that reduces to nothing matches nothing, not everything.</summary>
    [Fact]
    public void ASubjectTooShortToSearchMatchesNothing()
    {
        Assert.DoesNotContain("regularExpression", Render(FeedStrands.AboutSubjects(["New"])));
    }
}

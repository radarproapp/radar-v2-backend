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
}

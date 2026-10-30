using System.Reflection;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// What the Mentor writes down about a conversation, and what it refuses to write down.
/// </summary>
/// <remarks>
/// Mentor chat kept nothing at all until now: quizzes and study plans were stored from the day
/// they were written, and the conversation lived in the browser tab. The two things worth being
/// careful about while giving it a memory are what goes into it and who can read it, so both are
/// tested here rather than only through the database.
/// </remarks>
public class MentorHistoryTests
{
    // ── The title a thread gets ─────────────────────────────────────────────

    [Fact]
    public void AShortQuestionIsTheWholeTitle()
    {
        Assert.Equal(
            "What is a LEFT JOIN?",
            MongoLearningMentorService.TitleFrom("What is a LEFT JOIN?"));
    }

    /// <summary>
    /// A long one is cut at a word boundary, not at the character the limit happens to fall on.
    /// </summary>
    /// <remarks>
    /// A sidebar of threads reading "How do I calculate the standard deviation of a s" is harder
    /// to scan than one reading "How do I calculate the standard deviation of a...", and the
    /// difference costs nothing.
    /// </remarks>
    [Fact]
    public void ALongQuestionIsCutAtAWord()
    {
        var title = MongoLearningMentorService.TitleFrom(
            "How do I calculate the standard deviation of a sample rather than a population?");

        Assert.StartsWith("How do I calculate the standard deviation of a", title);
        Assert.EndsWith("...", title);
        Assert.DoesNotContain("populatio", title);

        // Cut between words: nothing before the ellipsis is half a word.
        Assert.True(
            title[..^3].Split(' ').All(word => word.Length > 0),
            $"Cut mid-word: {title}");
    }

    /// <summary>
    /// Whitespace is collapsed first, because a question is often pasted in over several lines.
    /// </summary>
    /// <remarks>
    /// Left alone, the newline travels into the title and the list draws a row that is mostly
    /// blank - which looks like a thread that failed to save rather than one with a long title.
    /// </remarks>
    [Fact]
    public void ATitleIsOneLine()
    {
        var title = MongoLearningMentorService.TitleFrom("Explain this:\n\n    SELECT *\n    FROM orders");

        Assert.Equal("Explain this: SELECT * FROM orders", title);
    }

    /// <summary>And something with no spaces in it at all still gets cut.</summary>
    [Fact]
    public void APasteWithNoWordsInItIsStillCut()
    {
        var title = MongoLearningMentorService.TitleFrom(new string('x', 400));

        Assert.True(title.Length < 70, $"Title ran to {title.Length} characters.");
    }

    /// <summary>
    /// A message of nothing but whitespace still has to leave something clickable behind.
    /// </summary>
    /// <remarks>
    /// The endpoint requires a message of at least one character, which a space satisfies. An
    /// empty title would be a row in the list with nothing in it to click.
    /// </remarks>
    [Fact]
    public void AnEmptyQuestionStillNamesTheThread()
    {
        Assert.NotEmpty(MongoLearningMentorService.TitleFrom("   "));
    }

    // ── The sentence that is not an answer ──────────────────────────────────

    /// <summary>
    /// The sentence the engine really streams when it cannot reach the model.
    /// </summary>
    /// <remarks>
    /// Read off the engine rather than retyped here. The store has no flag to go on - the stream
    /// is a sequence of strings - so it recognises the failure by its words, and a reword on the
    /// engine's side would otherwise silently start filing refusals as tutoring. This is the test
    /// that notices.
    /// </remarks>
    internal static string UnreachableSentence(string question)
    {
        var method = typeof(OpenRouterAiEngine).GetMethod(
            "GetDeterministicChatStream", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var chunks = (IEnumerable<string>)method!.Invoke(
            null,
            ["mentor-chat", "system", new List<ChatMessage> { new() { Role = "user", Content = question } }])!;

        return string.Concat(chunks);
    }

    /// <summary>
    /// Both forms of it: the engine names the subject when it can read one out of the question.
    /// </summary>
    [Theory]
    [InlineData("What roles does Mathematics lead to?")]
    [InlineData("Tell me about Bayes rule.")]
    public void TheEnginesRefusalIsRecognisedAsOne(string question)
    {
        var sentence = UnreachableSentence(question);

        Assert.True(
            MongoLearningMentorService.IsTutorUnreachable(sentence),
            $"Not recognised as a failure: {sentence}");
    }

    /// <summary>And a real reply is not mistaken for one.</summary>
    [Fact]
    public void ATutoringReplyIsNotMistakenForARefusal()
    {
        Assert.False(MongoLearningMentorService.IsTutorUnreachable(
            "A LEFT JOIN keeps every row from the left table and fills the missing right-hand "
            + "columns with nulls.\n\n**Your next move:** write one against the orders table."));
    }

    // ── Who a conversation belongs to ───────────────────────────────────────

    private static string Render(FilterDefinition<MentorConversation> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter
            .Render(new RenderArgs<MentorConversation>(registry.GetSerializer<MentorConversation>(), registry))
            .ToString();
    }

    /// <summary>The query names the conversation and the owner, both.</summary>
    [Fact]
    public void TheQueryAsksWhoOwnsIt()
    {
        var rendered = Render(MongoLearningMentorService.OwnedBy("conversation-1", "user-1"));

        Assert.Contains("conversation-1", rendered);
        Assert.Contains("user-1", rendered);
        Assert.Contains("UserId", rendered);
    }

    /// <summary>
    /// And one filter does the read, the append and the delete.
    /// </summary>
    /// <remarks>
    /// Three separately written filters are three chances for one of them to be missing the
    /// owner, and a read that is scoped beside a delete that is not is worse than neither being
    /// scoped, because nothing looks wrong from the outside. This is the same guard the notebook
    /// has, written for the same reason: it had that defect, in that shape.
    /// </remarks>
    [Fact]
    public void NoConversationQueryAsksOnlyForTheId()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var source = File.ReadAllText(
            Path.Combine(dir!.FullName, "Services", "MongoLearningMentorService.cs"));

        Assert.DoesNotContain("c => c.Id == conversationId)", source);

        // The read, the append of a turn, the delete, and settling which conversation a turn
        // belongs to before the reply streams. The fourth arrived with that last one and this
        // assertion is how: it failed at three, which is the check working - a new query that
        // takes a conversation id is exactly what has to be looked at.
        Assert.Equal(4, source.Split("OwnedBy(conversationId, userId)").Length - 1);
    }
}

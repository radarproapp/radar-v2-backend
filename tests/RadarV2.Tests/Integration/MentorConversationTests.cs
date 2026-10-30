using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Tests.Integration;

/// <summary>
/// The Mentor's conversation history, asked of a database.
/// </summary>
/// <remarks>
/// Mentor chat was the one part of the Mentor that stored nothing. Quizzes and study plans were
/// written down from the start; the conversation was held in the browser tab and posted back up
/// by the client on every turn, so closing the tab lost the explanation the learner was halfway
/// through along with every question they had asked to get there.
///
/// These tests run against a real mongod rather than a fake, because what is being claimed is
/// that a turn lands in the right document and that somebody else's thread is out of reach -
/// neither of which a hand-written double can demonstrate about the driver.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class MentorConversationTests
{
    private readonly RadarDatabase _db;
    private readonly StubTutor _tutor = new();
    private readonly MongoLearningMentorService _mentor;

    public MentorConversationTests(RadarDatabaseFixture fixture)
    {
        // Short on purpose: the fixture appends a GUID, and mongod refuses a database name over
        // 63 characters - which "mentor-conversations" was one character past.
        _db = fixture.Database("mentor-chat");

        // No HTTP factory: the chat path reaches the provider only through the engine, which is
        // stubbed here. A change that starts using the factory on this path fails loudly in this
        // test rather than quietly in production.
        _mentor = new MongoLearningMentorService(
            null!,
            new ConfigurationBuilder().Build(),
            _db,
            _tutor);
    }

    private const string Learner = "the-learner";
    private const string Stranger = "somebody-else";

    /// <summary>Reads a reply to the end, which is what the SSE writer does when nobody leaves.</summary>
    private async Task<string> AskAsync(string message, string? conversationId = null, string userId = Learner)
    {
        var reply = new StringBuilder();
        await foreach (var chunk in _mentor.StreamMessageAsync(userId, message, [], conversationId))
            reply.Append(chunk);

        return reply.ToString();
    }

    private async Task<string> IdOfAsync(string title, string userId = Learner) =>
        (await _mentor.GetConversationsAsync(userId)).Single(c => c.Title == title).Id;

    // ── Starting a thread ───────────────────────────────────────────────────

    [Fact]
    public async Task AFirstQuestionStartsAThreadNamedAfterIt()
    {
        _tutor.Replies("A LEFT JOIN keeps every row from the left table.");

        await AskAsync("What is a LEFT JOIN?");

        var started = Assert.Single(await _mentor.GetConversationsAsync(Learner));
        Assert.Equal("What is a LEFT JOIN?", started.Title);
        Assert.Equal(2, started.MessageCount);
    }

    /// <summary>Both halves of the turn are kept, in the order they happened.</summary>
    [Fact]
    public async Task TheQuestionAndTheReplyAreBothKept()
    {
        _tutor.Replies("An index is read in order, ", "so the rows come back already sorted.");

        await AskAsync("How does an index work?");

        var stored = await _mentor.GetConversationAsync(Learner, await IdOfAsync("How does an index work?"));

        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Messages.Count);
        Assert.Equal("user", stored.Messages[0].Role);
        Assert.Equal("How does an index work?", stored.Messages[0].Content);
        Assert.Equal("assistant", stored.Messages[1].Role);

        // Reassembled from the chunks as the reader saw them, not one chunk of it.
        Assert.Equal("An index is read in order, so the rows come back already sorted.", stored.Messages[1].Content);
    }

    // ── Joining the right thread ────────────────────────────────────────────

    /// <summary>
    /// A reply joins the conversation the question was asked in, and leaves the others alone.
    /// </summary>
    [Fact]
    public async Task AReplyJoinsTheThreadItWasAskedIn()
    {
        _tutor.Replies("Normalisation removes duplicated facts.");
        await AskAsync("What is normalisation?");

        _tutor.Replies("A view is a stored query.");
        await AskAsync("What is a view?");

        var first = await IdOfAsync("What is normalisation?");

        _tutor.Replies("Because the duplicate can be updated in one place and not the other.");
        await AskAsync("Why does duplication matter?", first);

        var continued = await _mentor.GetConversationAsync(Learner, first);
        Assert.NotNull(continued);
        Assert.Equal(4, continued!.Messages.Count);
        Assert.Equal("Why does duplication matter?", continued.Messages[2].Content);
        Assert.Equal(
            "Because the duplicate can be updated in one place and not the other.",
            continued.Messages[3].Content);

        // The thread it did not belong to is untouched, and still carries its own title.
        var other = (await _mentor.GetConversationsAsync(Learner)).Single(c => c.Title == "What is a view?");
        Assert.Equal(2, other.MessageCount);
    }

    /// <summary>
    /// The stored turns are what the model is shown when the client sends no history of its own.
    /// </summary>
    /// <remarks>
    /// This is the point of storing them. A thread reopened in a tab that was not the one it
    /// started in has nothing on screen to post back up, and a tutor that cannot see what was
    /// already said restarts the explanation from the beginning.
    /// </remarks>
    [Fact]
    public async Task AReopenedThreadIsPutToTheModel()
    {
        _tutor.Replies("A primary key identifies one row.");
        await AskAsync("What is a primary key?");
        var thread = await IdOfAsync("What is a primary key?");

        _tutor.Replies("It can, if the pair is unique together.");
        await AskAsync("Can it be two columns?", thread);

        var seen = _tutor.LastMessages.Select(m => m.Content).ToList();
        Assert.Contains("What is a primary key?", seen);
        Assert.Contains("A primary key identifies one row.", seen);
        Assert.Contains("Can it be two columns?", seen);

        // And the question that is about to be asked is not in there twice.
        Assert.Single(seen, c => c == "Can it be two columns?");
    }

    // ── What is not an answer, and is not stored as one ─────────────────────

    /// <summary>
    /// The sentence the engine streams when it cannot reach the model is not stored as tutoring.
    /// </summary>
    /// <remarks>
    /// The engine answers an unreachable model with "Radar could not reach its tutor just
    /// now..." and records the activity as success:false. Kept as an assistant turn it would
    /// stand as the answer to that question for as long as the thread exists, and go back up to
    /// the model next turn as something the tutor had said.
    ///
    /// The question stays, because the learner did ask it - and a question with nothing under it
    /// is both the truth about what happened and what they need in order to ask it again.
    /// </remarks>
    [Fact]
    public async Task TheTutorBeingUnreachableIsNotStoredAsAnAnswer()
    {
        const string question = "What roles does Mathematics lead to?";
        _tutor.Replies(MentorHistoryTests.UnreachableSentence(question));

        await AskAsync(question);

        var stored = await _mentor.GetConversationAsync(Learner, await IdOfAsync(question));

        Assert.NotNull(stored);
        var only = Assert.Single(stored!.Messages);
        Assert.Equal("user", only.Role);
        Assert.Equal(question, only.Content);
    }

    /// <summary>
    /// A reply cut off because the reader left is not stored either.
    /// </summary>
    /// <remarks>
    /// What survives a reader navigating away is a fragment ending mid-sentence with nothing in
    /// it that says so. Stored, history would present it as the whole of what the tutor said and
    /// the next turn would feed it back to the model as a finished answer.
    /// </remarks>
    [Fact]
    public async Task AReplyCutOffWhenTheReaderLeavesIsNotStored()
    {
        _tutor.Replies("A LEFT JOIN keeps ", "every row on the left ", "and nulls the rest.");

        using var leaving = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _mentor.StreamMessageAsync(Learner, "What is a LEFT JOIN?", [], null, leaving.Token))
            {
                // The tab closes once the first chunk has painted.
                leaving.Cancel();
            }
        });

        var thread = Assert.Single(await _mentor.GetConversationsAsync(Learner));
        Assert.Equal(1, thread.MessageCount);

        var stored = await _mentor.GetConversationAsync(Learner, thread.Id);
        Assert.Equal("user", Assert.Single(stored!.Messages).Role);
    }

    // ── Whose thread it is ──────────────────────────────────────────────────

    [Fact]
    public async Task AStrangerCannotReadIt()
    {
        _tutor.Replies("Here is what your appraisal should say.");
        await AskAsync("Help me write up why I deserve a raise");

        var thread = await IdOfAsync("Help me write up why I deserve a raise");

        Assert.Null(await _mentor.GetConversationAsync(Stranger, thread));
        Assert.NotNull(await _mentor.GetConversationAsync(Learner, thread));
    }

    [Fact]
    public async Task AStrangerCannotDeleteIt()
    {
        _tutor.Replies("Start with the units.");
        await AskAsync("Teach me stoichiometry");

        var thread = await IdOfAsync("Teach me stoichiometry");

        Assert.False(await _mentor.DeleteConversationAsync(Stranger, thread));
        Assert.NotNull(await _mentor.GetConversationAsync(Learner, thread));
    }

    /// <summary>A stranger cannot see it in their own list either.</summary>
    [Fact]
    public async Task AStrangerDoesNotSeeItListed()
    {
        _tutor.Replies("Start with the integral.");
        await AskAsync("Teach me calculus");

        Assert.Empty(await _mentor.GetConversationsAsync(Stranger));
    }

    /// <summary>
    /// A turn naming somebody else's thread starts a thread of the caller's own.
    /// </summary>
    /// <remarks>
    /// It cannot join it, and it must not fail either: an id arriving from a client that has been
    /// signed in as two accounts in one browser is a stale id, not an attack, and losing the
    /// learner's question over it would be the worse of the two outcomes. What it must never do
    /// is put a turn into, or read a turn out of, the other account's thread.
    /// </remarks>
    [Fact]
    public async Task AnIdThatIsNotYoursStartsAThreadOfYourOwn()
    {
        _tutor.Replies("The mean is the total over the count.");
        await AskAsync("What is the mean?");
        var theirs = await IdOfAsync("What is the mean?");

        _tutor.Replies("The median is the middle value.");
        await AskAsync("What is the median?", theirs, Stranger);

        // Theirs is where they left it.
        var untouched = await _mentor.GetConversationAsync(Learner, theirs);
        Assert.Equal(2, untouched!.Messages.Count);
        Assert.DoesNotContain(untouched.Messages, m => m.Content.Contains("median"));

        // And the stranger's question is in a thread of the stranger's own.
        var started = Assert.Single(await _mentor.GetConversationsAsync(Stranger));
        Assert.NotEqual(theirs, started.Id);
        Assert.Equal("What is the median?", started.Title);
    }

    [Fact]
    public async Task TheOwnerCanDeleteTheirOwnThread()
    {
        _tutor.Replies("Here it is.");
        await AskAsync("Explain recursion");
        var thread = await IdOfAsync("Explain recursion");

        Assert.True(await _mentor.DeleteConversationAsync(Learner, thread));
        Assert.Null(await _mentor.GetConversationAsync(Learner, thread));
        Assert.Empty(await _mentor.GetConversationsAsync(Learner));
    }

    /// <summary>An id that is nobody's conversation is simply not there.</summary>
    [Fact]
    public async Task AnIdThatIsNotAThreadIsNotFound()
    {
        Assert.Null(await _mentor.GetConversationAsync(Learner, "no-such-thread"));
        Assert.False(await _mentor.DeleteConversationAsync(Learner, "no-such-thread"));
    }

    // ── The list ────────────────────────────────────────────────────────────

    /// <summary>
    /// The list is ordered by the last turn taken, newest first.
    /// </summary>
    /// <remarks>
    /// Seeded with explicit times rather than by chatting three times: DateTime.UtcNow moves in
    /// steps of about fifteen milliseconds on Windows, so three conversations started in a row by
    /// a test would tie on the field being sorted and the order would be whatever the server
    /// happened to return.
    /// </remarks>
    [Fact]
    public async Task TheListIsNewestFirstByItsLastTurn()
    {
        var started = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        await _db.MentorConversations.InsertManyAsync(
        [
            new MentorConversation
            {
                UserId = Learner, Title = "Oldest", CreatedAt = started,
                LastMessageAt = started.AddHours(1),
            },
            new MentorConversation
            {
                UserId = Learner, Title = "Newest", CreatedAt = started,
                LastMessageAt = started.AddHours(9),
            },
            new MentorConversation
            {
                UserId = Learner, Title = "Middle", CreatedAt = started,
                LastMessageAt = started.AddHours(5),
            },
        ]);

        var listed = await _mentor.GetConversationsAsync(Learner);

        Assert.Equal(["Newest", "Middle", "Oldest"], listed.Select(c => c.Title));
    }

    /// <summary>
    /// Carrying on an old thread brings it back to the top.
    /// </summary>
    /// <remarks>
    /// The sort is on the last turn rather than on when the thread was started, so a
    /// conversation picked up again a week later comes back where its reader left it.
    ///
    /// The thread it has to overtake is seeded with a time of its own rather than started by
    /// chatting, for the same reason as the test above: two turns taken in a row by a test can
    /// land on the same clock tick, and then this would be asserting on a coin toss.
    /// </remarks>
    [Fact]
    public async Task AThreadCarriedOnComesBackToTheTop()
    {
        var lastWeek = DateTime.UtcNow.AddDays(-7);

        var old = new MentorConversation
        {
            UserId = Learner,
            Title = "Started last week",
            CreatedAt = lastWeek,
            LastMessageAt = lastWeek,
            Messages = [new ChatMessage { Role = "user", Content = "Where were we?", Timestamp = lastWeek }],
        };

        await _db.MentorConversations.InsertManyAsync(
        [
            old,
            new MentorConversation
            {
                UserId = Learner,
                Title = "Started yesterday",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                LastMessageAt = DateTime.UtcNow.AddDays(-1),
            },
        ]);

        _tutor.Replies("We were on normal forms.");
        await AskAsync("Where were we?", old.Id);

        var listed = await _mentor.GetConversationsAsync(Learner);
        Assert.Equal("Started last week", listed[0].Title);
        Assert.Equal(3, listed[0].MessageCount);
        Assert.True(listed[0].LastMessageAt > lastWeek, "The thread kept its old timestamp.");
    }

    /// <summary>
    /// A tutor that streams whatever the test hands it, in the chunks the test hands it in.
    /// </summary>
    private sealed class StubTutor : IAiEngine
    {
        private string[] _chunks = ["An answer."];

        public void Replies(params string[] chunks) => _chunks = chunks;

        /// <summary>What the service put in front of the model on the last turn.</summary>
        public List<ChatMessage> LastMessages { get; private set; } = [];

        public async IAsyncEnumerable<string> StreamChatAsync(
            string feature, string systemPrompt, IReadOnlyCollection<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastMessages = [.. messages];

            foreach (var chunk in _chunks)
            {
                // Honours the token the way the real engine does, so a test can leave mid-stream.
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }

        /// <summary>
        /// Everything below throws, because the chat path reaches none of it.
        /// </summary>
        /// <remarks>
        /// The prompt is built out of the profile, the roadmap and the mastery records, all of
        /// which are database reads. If a change starts generating something here, these tests
        /// say so instead of quietly passing against a stub that answered.
        /// </remarks>
        public Task<string?> GenerateTextAsync(
            string feature, string systemPrompt, string userPrompt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<T?> GenerateJsonAsync<T>(
            string feature, string systemPrompt, string userPrompt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool TryGetCachedJson<T>(
            string feature, string systemPrompt, string userPrompt, out T? value) =>
            throw new NotSupportedException();

        public void WarmJson<T>(string feature, string systemPrompt, string userPrompt) =>
            throw new NotSupportedException();

        public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RecordActivityAsync(
            string feature, string? userId, string mode, bool success, long durationMs,
            string? summary = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<AiEngineStats> GetEngineStatsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}

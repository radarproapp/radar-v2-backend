using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Tests;

/// <summary>
/// A failed judgement must not be recorded as a finding about the corpus.
///
/// The gate was written on the assumption that the engine returns null when it cannot answer. It
/// never does: every failure falls through to a deterministic brain, and for a feature that brain
/// has no template for, it synthesises "{}". Deserialised into a response whose list was
/// initialised to empty, that is indistinguishable from the model having read every candidate and
/// kept none.
///
/// So a rotated key or a spent monthly cap read as "nothing in the library teaches this lesson",
/// and that was written down permanently, on every lesson of every path, in a single pass — taking
/// with it the resources the whole change exists to replace. The retry branch was unreachable.
///
/// These tests exist because every other test in the suite passed while that was true.
/// </summary>
public class LessonJudgementFailureTests
{
    private static LessonResourceJudge JudgeReturning(string json) =>
        new(new StubEngine(json), NullLogger<LessonResourceJudge>.Instance);

    private static IReadOnlyList<LessonResourceJudge.Candidate> OneCandidate() =>
        [new("item-1", "A title", "A source", "A summary")];

    /// <summary>
    /// The exact body the deterministic brain produces for this feature.
    /// </summary>
    [Fact]
    public async Task AnEmptyBodyIsNotAnAnswer()
    {
        var verdicts = await JudgeReturning("{}").JudgeAsync(
            "Understanding Numbers", "Counting and comparing", "Learn mathematics", [], OneCandidate());

        Assert.Null(verdicts);
    }

    /// <summary>
    /// And the case it has to stay distinguishable from: the model really did read them and keep
    /// none, which is a finished answer and settles the lesson.
    /// </summary>
    [Fact]
    public async Task KeepingNoneIsAnAnswer()
    {
        var verdicts = await JudgeReturning("""{"items":[]}""").JudgeAsync(
            "Understanding Numbers", "Counting and comparing", "Learn mathematics", [], OneCandidate());

        Assert.NotNull(verdicts);
        Assert.Empty(verdicts!);
    }

    [Fact]
    public async Task AKeptItemComesBack()
    {
        var verdicts = await JudgeReturning(
            """{"items":[{"id":"item-1","keep":true,"covers":"Walks through place value with worked examples.","why":"Gives you the notation this lesson assumes."}]}""")
            .JudgeAsync("Understanding Numbers", "Counting", "Learn mathematics", [], OneCandidate());

        Assert.NotNull(verdicts);
        Assert.Single(verdicts!);

        // Two fields, because a learner needs both: what is in it, and what it gives them here.
        Assert.Equal("Walks through place value with worked examples.", verdicts![0].Covers);
        Assert.Equal("Gives you the notation this lesson assumes.", verdicts[0].Why);
    }

    /// <summary>A thrown engine is not an answer either.</summary>
    [Fact]
    public async Task AnEngineThatThrowsIsNotAnAnswer()
    {
        var judge = new LessonResourceJudge(
            new StubEngine(null), NullLogger<LessonResourceJudge>.Instance);

        Assert.Null(await judge.JudgeAsync("A lesson", "", "a goal", [], OneCandidate()));
    }

    /// <summary>
    /// The rest of the path reaches the judge.
    /// </summary>
    /// <remarks>
    /// A learner building a path in Architecture was offered resources on layered architecture.
    /// A lesson called "Introduction to Architecture" is genuinely ambiguous on its own, and the
    /// thing that settles it — the lessons sitting beside it — was on the same page and was not
    /// being passed.
    /// </remarks>
    [Fact]
    public async Task TheRestOfThePathIsPutToTheJudge()
    {
        var engine = new StubEngine("""{"items":[]}""");
        var judge = new LessonResourceJudge(engine, NullLogger<LessonResourceJudge>.Instance);

        await judge.JudgeAsync(
            "Introduction to Architecture",
            "What the discipline covers",
            "Architecture",
            ["Site analysis and orientation", "Building regulations and approvals"],
            OneCandidate());

        Assert.Contains("Site analysis and orientation", engine.LastUserPrompt);
        Assert.Contains("Building regulations and approvals", engine.LastUserPrompt);
    }

    /// <summary>A lesson standing alone says so, rather than claiming an empty path.</summary>
    [Fact]
    public async Task ALoneLessonSaysItIsAlone()
    {
        var engine = new StubEngine("""{"items":[]}""");
        var judge = new LessonResourceJudge(engine, NullLogger<LessonResourceJudge>.Instance);

        await judge.JudgeAsync("A lesson", "", "a goal", [], OneCandidate());

        Assert.Contains("only lesson on the path", engine.LastUserPrompt);
    }

    /// <summary>
    /// Returns whatever JSON it is given, or throws when given none.
    /// </summary>
    private sealed class StubEngine(string? json) : IAiEngine
    {
        /// <summary>The last prompt it was asked, so a test can check what the judge was told.</summary>
        public string LastUserPrompt { get; private set; } = string.Empty;

        public Task<T?> GenerateJsonAsync<T>(
            string feature, string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            LastUserPrompt = userPrompt;

            if (json is null) throw new InvalidOperationException("the provider is unreachable");

            return Task.FromResult(JsonSerializer.Deserialize<T>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        }

        public Task<string?> GenerateTextAsync(
            string feature, string systemPrompt, string userPrompt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool TryGetCachedJson<T>(
            string feature, string systemPrompt, string userPrompt, out T? value) =>
            throw new NotSupportedException();

        public void WarmJson<T>(string feature, string systemPrompt, string userPrompt) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamChatAsync(
            string feature, string systemPrompt, IReadOnlyCollection<ChatMessage> messages,
            CancellationToken ct = default) =>
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

using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// The half of the campaign a person has to judge.
/// </summary>
/// <remarks>
/// Radar cannot see a video on TikTok, so every number on this board came from a reviewer who
/// watched it. The distinction these tests exist to protect is between "scored badly" and "not
/// looked at yet" - two states a single integer cannot hold, and which a board that confuses
/// them reports to a participant as a rejection.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class ContentSubmissionTests
{
    private readonly RadarDatabase _db;
    private readonly ContentSubmissionService _content;

    public ContentSubmissionTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("content-submissions");
        _content = new ContentSubmissionService(_db);
    }

    private static UserProfile Person(string id, string name) => new() { Id = id, Name = name };

    private async Task<ContentSubmission> Submitted(string profileId, string name, string link = "https://tiktok.com/@ada/video/1")
        => await _content.SubmitAsync(Person(profileId, name), link, "TikTok", "Explains the feed", default);

    [Fact]
    public async Task ASubmissionStartsUnjudged()
    {
        var submission = await Submitted("ada", "Ada");

        var mine = await _content.MineAsync("ada");

        var stored = Assert.Single(mine);
        Assert.Equal(submission.Id, stored.Id);
        Assert.Null(stored.Score);
        Assert.Null(stored.Verdict);
        Assert.False(stored.IsJudged);
    }

    /// <summary>Nobody has looked at it, and that is not the same as a zero.</summary>
    /// <remarks>
    /// Written as a raw-document assertion because the distinction only survives if the field is
    /// genuinely absent. A zero, or an explicit null the board later treats as one, is Radar
    /// telling a participant their work was rejected while it sits in a queue.
    /// </remarks>
    [Fact]
    public async Task AnUnjudgedSubmissionHasNoScoreAtAllRatherThanAZero()
    {
        await Submitted("ada", "Ada");

        var raw = await _db.ContentSubmissions
            .Find(FilterDefinition<ContentSubmission>.Empty)
            .Project(Builders<ContentSubmission>.Projection.As<BsonDocument>())
            .FirstAsync();

        Assert.False(raw.Contains("Score"));
        Assert.False(raw.Contains("Verdict"));
    }

    [Fact]
    public async Task AReviewersMarkAndReasonAreBothKept()
    {
        var submission = await Submitted("ada", "Ada");

        var judged = await _content.JudgeAsync(submission.Id, "admin-1",
            new ContentSubmissionService.Judgement(8, "Clear, and it shows the actual product"));

        Assert.True(judged);

        var stored = Assert.Single(await _content.MineAsync("ada"));
        Assert.Equal(8, stored.Score);
        Assert.Equal("Clear, and it shows the actual product", stored.Verdict);
        Assert.Equal("admin-1", stored.JudgedByProfileId);
        Assert.NotNull(stored.JudgedAt);
        Assert.True(stored.IsJudged);
    }

    [Fact]
    public async Task JudgingSomethingThatIsNotThereSaysSoRatherThanSucceedingQuietly()
    {
        var judged = await _content.JudgeAsync("no-such-submission", "admin-1",
            new ContentSubmissionService.Judgement(8, "Nothing to judge here"));

        Assert.False(judged);
    }

    /// <summary>A reviewer opens the queue to see what nobody has looked at.</summary>
    [Fact]
    public async Task TheQueuePutsUnjudgedWorkFirst()
    {
        var old = await Submitted("ada", "Ada", "https://tiktok.com/@ada/video/1");
        await _content.JudgeAsync(old.Id, "admin-1", new ContentSubmissionService.Judgement(9, "Already looked at this one"));
        var waiting = await Submitted("obi", "Obi", "https://tiktok.com/@obi/video/2");

        var queue = await _content.QueueAsync();

        Assert.Equal([waiting.Id, old.Id], queue.Select(c => c.Id));
    }

    /// <summary>
    /// The board ranks on the best single piece, not on how many links somebody posted.
    /// </summary>
    /// <remarks>
    /// The campaign is asking who made something good. Totalling instead would mean ten
    /// forgettable clips beat one video that actually explains Radar, which is the opposite of
    /// what is being rewarded.
    /// </remarks>
    [Fact]
    public async Task OneGoodPieceBeatsManyWeakOnes()
    {
        foreach (var i in Enumerable.Range(1, 5))
        {
            var weak = await Submitted("obi", "Obi", $"https://tiktok.com/@obi/video/{i}");
            await _content.JudgeAsync(weak.Id, "admin-1", new ContentSubmissionService.Judgement(4, "Hard to follow, and it never shows the feed"));
        }

        var strong = await Submitted("ada", "Ada");
        await _content.JudgeAsync(strong.Id, "admin-1", new ContentSubmissionService.Judgement(9, "Shows a real feed and says why it is ranked that way"));

        var board = await _content.BoardAsync();

        Assert.Equal(["ada", "obi"], board.Makers.Select(m => m.ProfileId));
        Assert.Equal(9, board.Makers[0].Best);
        Assert.Equal(20, board.Makers[1].Total);
        Assert.Equal(5, board.Makers[1].Submissions);
    }

    /// <summary>Unreviewed work is counted out loud, not dropped.</summary>
    /// <remarks>
    /// A board of one name with thirty submissions waiting reads as a campaign nobody entered,
    /// and whoever is running it would draw exactly the wrong conclusion from the silence.
    /// </remarks>
    [Fact]
    public async Task WorkNobodyHasLookedAtIsCountedRatherThanRankedAsZero()
    {
        var judged = await Submitted("ada", "Ada");
        await _content.JudgeAsync(judged.Id, "admin-1", new ContentSubmissionService.Judgement(7, "Good, if a little long"));

        await Submitted("obi", "Obi", "https://tiktok.com/@obi/video/2");
        await Submitted("chi", "Chidi", "https://tiktok.com/@chi/video/3");

        var board = await _content.BoardAsync();

        Assert.Equal(["ada"], board.Makers.Select(m => m.ProfileId));
        Assert.Equal(2, board.AwaitingReview);
    }

    /// <summary>An empty campaign says nothing rather than inventing a leader.</summary>
    [Fact]
    public async Task AnEmptyBoardIsEmpty()
    {
        var board = await _content.BoardAsync();

        Assert.Empty(board.Makers);
        Assert.Equal(0, board.AwaitingReview);
    }
}

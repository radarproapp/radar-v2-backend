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

        // Built here rather than left to the database's own setup, which runs fire-and-forget
        // from the constructor - so relying on it would make this a race.
        _db.ContentSubmissions.Indexes.CreateOne(
            new CreateIndexModel<ContentSubmission>(
                Builders<ContentSubmission>.IndexKeys
                    .Ascending(c => c.ProfileId)
                    .Ascending(c => c.LinkKey),
                new CreateIndexOptions { Unique = true }));
    }

    private static UserProfile Person(string id, string name) => new() { Id = id, Name = name };

    private async Task<ContentSubmission> Submitted(string profileId, string name, string link = "https://tiktok.com/@ada/video/1")
    {
        var (result, submission) = await _content.SubmitAsync(Person(profileId, name), link, "TikTok", "Explains the feed");
        Assert.Equal(ContentSubmissionService.SubmitResult.Accepted, result);
        return submission!;
    }

    private async Task Judged(ContentSubmission submission, int score, string verdict, string by = "admin-1") =>
        Assert.Equal(
            ContentSubmissionService.JudgeResult.Recorded,
            await _content.JudgeAsync(submission.Id, by, new ContentSubmissionService.Judgement(score, verdict)));

    // ── submitting ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ASubmissionStartsUnjudged()
    {
        var submission = await Submitted("ada", "Ada");

        var stored = Assert.Single(await _content.MineAsync("ada"));

        Assert.Equal(submission.Id, stored.Id);
        Assert.Null(stored.Score);
        Assert.Null(stored.Verdict);
        Assert.False(stored.IsJudged);
        Assert.Empty(stored.Marks);
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

    /// <summary>A link nobody could open is refused rather than queued.</summary>
    [Fact]
    public async Task ALinkThatIsNotALinkIsRefused()
    {
        var (result, submission) = await _content.SubmitAsync(Person("ada", "Ada"), "javascript:alert(1)", "TikTok", "");

        Assert.Equal(ContentSubmissionService.SubmitResult.LinkNotUsable, result);
        Assert.Null(submission);
        Assert.Empty(await _content.MineAsync("ada"));
    }

    /// <summary>
    /// One video, once.
    /// </summary>
    /// <remarks>
    /// Submitting the same link five times was free, and the board summed each participant's
    /// marks - so five copies of one video marked nine beat one video marked nine. The ranking
    /// no longer sums, and the duplicate no longer lands.
    /// </remarks>
    [Fact]
    public async Task TheSameLinkTwiceIsRefusedTheSecondTime()
    {
        await Submitted("ada", "Ada", "https://www.tiktok.com/@ada/video/1");

        var (result, _) = await _content.SubmitAsync(
            Person("ada", "Ada"), "https://www.TikTok.com/@ada/video/1/", "TikTok", "Same one again");

        Assert.Equal(ContentSubmissionService.SubmitResult.AlreadySubmitted, result);
        Assert.Single(await _content.MineAsync("ada"));
    }

    /// <summary>Two people may submit the same link; it is one claim each.</summary>
    [Fact]
    public async Task TwoPeopleMaySubmitTheSameLink()
    {
        await Submitted("ada", "Ada", "https://tiktok.com/@radar/video/9");
        await Submitted("obi", "Obi", "https://tiktok.com/@radar/video/9");

        Assert.Single(await _content.MineAsync("ada"));
        Assert.Single(await _content.MineAsync("obi"));
    }

    /// <summary>A mistake can be taken back, until somebody has marked it.</summary>
    /// <remarks>
    /// There was no way to undo a wrong link: it stayed in the queue and then on the board for
    /// good. Not after judging, because deleting a marked submission deletes a reviewer's record
    /// of having marked it.
    /// </remarks>
    [Fact]
    public async Task AnUnjudgedSubmissionCanBeTakenBack()
    {
        var submission = await Submitted("ada", "Ada");

        Assert.Equal(ContentSubmissionService.WithdrawResult.Withdrawn, await _content.WithdrawAsync(submission.Id, "ada"));
        Assert.Empty(await _content.MineAsync("ada"));
    }

    [Fact]
    public async Task AJudgedSubmissionCannotBeTakenBack()
    {
        var submission = await Submitted("ada", "Ada");
        await Judged(submission, 7, "Good, if a little long");

        Assert.Equal(ContentSubmissionService.WithdrawResult.AlreadyJudged, await _content.WithdrawAsync(submission.Id, "ada"));
        Assert.Single(await _content.MineAsync("ada"));
    }

    [Fact]
    public async Task NobodyCanTakeBackSomebodyElsesSubmission()
    {
        var submission = await Submitted("ada", "Ada");

        Assert.Equal(ContentSubmissionService.WithdrawResult.NotFound, await _content.WithdrawAsync(submission.Id, "obi"));
        Assert.Single(await _content.MineAsync("ada"));
    }

    // ── reviewing ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AReviewersMarkAndReasonAreBothKept()
    {
        var submission = await Submitted("ada", "Ada");
        await Judged(submission, 8, "Clear, and it shows the actual product");

        var stored = Assert.Single(await _content.MineAsync("ada"));

        Assert.Equal(8, stored.Score);
        Assert.Equal("Clear, and it shows the actual product", stored.Verdict);
        Assert.Equal("admin-1", stored.JudgedByProfileId);
        Assert.NotNull(stored.JudgedAt);
        Assert.True(stored.IsJudged);
    }

    [Fact]
    public async Task JudgingSomethingThatIsNotThereSaysSoRatherThanSucceedingQuietly() =>
        Assert.Equal(
            ContentSubmissionService.JudgeResult.NotFound,
            await _content.JudgeAsync("no-such-submission", "admin-1",
                new ContentSubmissionService.Judgement(8, "Nothing to judge here")));

    /// <summary>
    /// Nobody marks their own work.
    /// </summary>
    /// <remarks>
    /// An admin is a participant too - submitting only asks that a caller be signed in - so an
    /// admin could post a link and award it a ten. The one field recording who did it was never
    /// read back by anything, so the board would have shown them at rank one with nothing
    /// anywhere to say how they got there.
    /// </remarks>
    [Fact]
    public async Task AReviewerCannotMarkTheirOwnSubmission()
    {
        var submission = await Submitted("admin-1", "The Reviewer");

        Assert.Equal(
            ContentSubmissionService.JudgeResult.CannotJudgeOwnWork,
            await _content.JudgeAsync(submission.Id, "admin-1", new ContentSubmissionService.Judgement(10, "If I say so myself")));

        var stored = Assert.Single(await _content.MineAsync("admin-1"));
        Assert.Null(stored.Score);
        Assert.Empty((await _content.BoardAsync()).Makers);
    }

    /// <summary>
    /// A mark that replaces another does not erase it.
    /// </summary>
    /// <remarks>
    /// A second judging used to overwrite the first with no trace, so a participant shown a nine
    /// by a person could have the record say two, with the original reviewer's reason gone. On a
    /// board that decides a prize, a number has to stay arguable.
    /// </remarks>
    [Fact]
    public async Task AnEarlierMarkSurvivesBeingReplaced()
    {
        var submission = await Submitted("ada", "Ada");
        await Judged(submission, 9, "Shows a real feed and says why it ranked that way", by: "admin-1");
        await Judged(submission, 2, "On a second look it is somebody else's footage", by: "admin-2");

        var stored = Assert.Single(await _content.MineAsync("ada"));

        Assert.Equal(2, stored.Score);
        Assert.Equal("admin-2", stored.JudgedByProfileId);
        Assert.Equal(2, stored.Marks.Count);
        Assert.Equal(9, stored.Marks[0].Score);
        Assert.Equal("admin-1", stored.Marks[0].ByProfileId);
        Assert.Equal("Shows a real feed and says why it ranked that way", stored.Marks[0].Verdict);
    }

    /// <summary>A zero is not a mark, whatever route it arrives by.</summary>
    /// <remarks>
    /// The endpoint's [Range(1,10)] was the only thing keeping a zero out of the store, and a
    /// stored zero satisfies every "has this been looked at" test in the system while
    /// contributing nothing to a best score.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task AMarkOutsideOneToTenIsRefusedBelowTheEndpointToo(int score)
    {
        var submission = await Submitted("ada", "Ada");

        Assert.Equal(
            ContentSubmissionService.JudgeResult.ScoreOutOfRange,
            await _content.JudgeAsync(submission.Id, "admin-1", new ContentSubmissionService.Judgement(score, "A reason long enough")));

        Assert.Null(Assert.Single(await _content.MineAsync("ada")).Score);
    }

    // ── the queue ────────────────────────────────────────────────────────────

    /// <summary>A reviewer opens the queue to see what has waited longest.</summary>
    [Fact]
    public async Task TheQueueIsWhatNobodyHasMarked()
    {
        var alreadySeen = await Submitted("ada", "Ada", "https://tiktok.com/@ada/video/1");
        await Judged(alreadySeen, 9, "Already looked at this one");
        var waiting = await Submitted("obi", "Obi", "https://tiktok.com/@obi/video/2");

        var queue = await _content.QueueAsync();

        Assert.Equal([waiting.Id], queue.Waiting.Select(c => c.Id));
        Assert.Equal([alreadySeen.Id], queue.RecentlyJudged.Select(c => c.Id));
    }

    /// <summary>
    /// Unmarked work cannot be buried by the volume of marked work.
    /// </summary>
    /// <remarks>
    /// The queue was the newest N submissions reordered to put unjudged first, so once N judged
    /// submissions existed, older unjudged work was unreachable at every value of the limit -
    /// while the board went on announcing it as awaiting review. Measured at the real cap: 520
    /// submissions, the oldest 20 unjudged, and no limit reached any of them.
    /// </remarks>
    [Fact]
    public async Task OlderUnmarkedWorkIsStillReachableBehindNewerMarkedWork()
    {
        var oldest = await Submitted("ada", "Ada", "https://tiktok.com/@ada/video/0");

        foreach (var i in Enumerable.Range(1, 30))
        {
            var newer = await Submitted("obi", "Obi", $"https://tiktok.com/@obi/video/{i}");
            await Judged(newer, 5, "Fine, nothing special about it");
        }

        var queue = await _content.QueueAsync(limit: 5);

        Assert.Equal([oldest.Id], queue.Waiting.Select(c => c.Id));
        Assert.Equal(1, (await _content.BoardAsync()).AwaitingReview);
    }

    // ── the board ────────────────────────────────────────────────────────────

    /// <summary>
    /// The board ranks on the best single piece, and on nothing else.
    /// </summary>
    /// <remarks>
    /// The campaign is asking who made something good. Summing the marks - which is what the
    /// tiebreak used to be - meant five copies of one clip marked nine beat one video marked
    /// nine, and every other tiebreak tried rewarded volume in some form too. Equal best marks
    /// are left equal, for whoever runs the campaign to look at.
    /// </remarks>
    [Fact]
    public async Task OneGoodPieceBeatsManyWeakOnes()
    {
        foreach (var i in Enumerable.Range(1, 5))
        {
            var weak = await Submitted("obi", "Obi", $"https://tiktok.com/@obi/video/{i}");
            await Judged(weak, 4, "Hard to follow, and it never shows the feed");
        }

        var strong = await Submitted("ada", "Ada");
        await Judged(strong, 9, "Shows a real feed and says why it is ranked that way");

        var board = await _content.BoardAsync();

        Assert.Equal(["ada", "obi"], board.Makers.Select(m => m.ProfileId));
        Assert.Equal(9, board.Makers[0].Best);
        Assert.Equal(4, board.Makers[1].Best);
    }

    /// <summary>
    /// What somebody submitted and what somebody marked are different facts.
    /// </summary>
    /// <remarks>
    /// This was one field, counted from the judged rows and labelled "submissions", so a
    /// participant with nine pieces and one reviewed read as having made one. A client had no
    /// way to tell.
    /// </remarks>
    [Fact]
    public async Task SubmittedAndJudgedAreCountedSeparately()
    {
        var judged = await Submitted("ada", "Ada", "https://tiktok.com/@ada/video/0");
        await Judged(judged, 9, "Shows a real feed and explains the ranking");

        foreach (var i in Enumerable.Range(1, 8))
            await Submitted("ada", "Ada", $"https://tiktok.com/@ada/video/{i}");

        var ada = Assert.Single((await _content.BoardAsync()).Makers);

        Assert.Equal(9, ada.Best);
        Assert.Equal(1, ada.Judged);
        Assert.Equal(9, ada.Submitted);
    }

    /// <summary>Unreviewed work is counted out loud, not ranked as zero.</summary>
    [Fact]
    public async Task WorkNobodyHasLookedAtIsCountedRatherThanRankedAsZero()
    {
        var judged = await Submitted("ada", "Ada");
        await Judged(judged, 7, "Good, if a little long");

        await Submitted("obi", "Obi", "https://tiktok.com/@obi/video/2");
        await Submitted("chi", "Chidi", "https://tiktok.com/@chi/video/3");

        var board = await _content.BoardAsync();

        Assert.Equal(["ada"], board.Makers.Select(m => m.ProfileId));
        Assert.Equal(2, board.AwaitingReview);
    }

    /// <summary>
    /// A row with no name says so, rather than being blank.
    /// </summary>
    /// <remarks>
    /// A profile's name starts empty and stays empty until the person answers, which is this
    /// product's own rule - so a nameless row on a prize-bearing board is the ordinary case. The
    /// board also prefers whichever name the participant did give, across their submissions.
    /// </remarks>
    [Fact]
    public async Task AParticipantWithNoNameIsNamedAnyway()
    {
        var nameless = await Submitted("zoe", "", "https://tiktok.com/@zoe/video/1");
        await Judged(nameless, 10, "The best one in the whole campaign");

        var zoe = Assert.Single((await _content.BoardAsync()).Makers);
        Assert.Equal("Unnamed participant", zoe.Name);
    }

    [Fact]
    public async Task ANameGivenLaterIsTheOneTheBoardUses()
    {
        var before = await Submitted("zoe", "", "https://tiktok.com/@zoe/video/1");
        await Judged(before, 6, "Decent, a bit quiet in places");

        var after = await Submitted("zoe", "Zoe", "https://tiktok.com/@zoe/video/2");
        await Judged(after, 8, "Much clearer than the first one");

        Assert.Equal("Zoe", Assert.Single((await _content.BoardAsync()).Makers).Name);
    }

    /// <summary>
    /// A row written before Score became absent rather than null still counts correctly.
    /// </summary>
    /// <remarks>
    /// The driver writes an explicit null unless a field carries [BsonIgnoreIfNull], so any
    /// document stored before that attribute existed has <c>Score: null</c>. It has to count as
    /// awaiting review and stay off the board; $ne and $eq against null treat a missing field
    /// and an explicit null alike, which is what makes that true.
    /// </remarks>
    [Fact]
    public async Task ALegacyRowHoldingAnExplicitNullCountsAsAwaitingReview()
    {
        await _db.ContentSubmissions.Database
            .GetCollection<BsonDocument>("content_submissions")
            .InsertOneAsync(new BsonDocument
            {
                ["_id"] = "legacy",
                ["ProfileId"] = "ada",
                ["ProfileName"] = "Ada",
                ["Link"] = "https://tiktok.com/@ada/video/old",
                ["LinkKey"] = "https://tiktok.com/@ada/video/old",
                ["Platform"] = "TikTok",
                ["Note"] = "",
                ["SubmittedAt"] = DateTime.UtcNow.AddDays(-30),
                ["Score"] = BsonNull.Value,
                ["Verdict"] = BsonNull.Value,
                ["Marks"] = new BsonArray(),
            });

        var board = await _content.BoardAsync();
        var queue = await _content.QueueAsync();

        Assert.Empty(board.Makers);
        Assert.Equal(1, board.AwaitingReview);
        Assert.Equal(["legacy"], queue.Waiting.Select(c => c.Id));
    }

    /// <summary>An empty campaign says nothing rather than inventing a leader.</summary>
    [Fact]
    public async Task AnEmptyBoardIsEmpty()
    {
        var board = await _content.BoardAsync();

        Assert.Empty(board.Makers);
        Assert.Equal(0, board.AwaitingReview);

        var queue = await _content.QueueAsync();
        Assert.Empty(queue.Waiting);
        Assert.Empty(queue.RecentlyJudged);
    }
}

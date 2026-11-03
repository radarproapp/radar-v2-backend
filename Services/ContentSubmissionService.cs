using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// The half of the campaign Radar cannot measure.
/// </summary>
/// <remarks>
/// A video lives on TikTok or WhatsApp. Nothing inside this product can see it, count its views,
/// or know whether it is any good - so every number on this board came from a person who watched
/// it and said so. There is no automatic score anywhere in this file.
///
/// Kept apart from <see cref="ReferralService"/> on purpose. Combining the two would need an
/// exchange rate between one video and one user, and there isn't an honest one; the question
/// "should an excellent video beat thirty real users" belongs to whoever runs the campaign,
/// answered in front of both boards, not buried in a weighting here.
/// </remarks>
public class ContentSubmissionService
{
    private readonly RadarDatabase _db;

    public ContentSubmissionService(RadarDatabase db) => _db = db;

    /// <summary>A mark on one submission, and the reviewer's reason for it.</summary>
    public record Judgement(int Score, string Verdict);

    /// <summary>What happened when something was submitted.</summary>
    public enum SubmitResult { Accepted, LinkNotUsable, AlreadySubmitted }

    /// <summary>What happened when a reviewer tried to mark something.</summary>
    /// <remarks>
    /// <see cref="CannotJudgeOwnWork"/> is its own answer rather than a quiet success. An admin
    /// is a participant too - the submission endpoint only asks that a caller be signed in - so
    /// without this an admin could post a link and award it a ten, and nothing in the stored
    /// record would have said who did it.
    /// </remarks>
    public enum JudgeResult { Recorded, NotFound, CannotJudgeOwnWork, ScoreOutOfRange }

    /// <summary>What happened when a participant tried to take something back.</summary>
    public enum WithdrawResult { Withdrawn, NotFound, AlreadyJudged }

    /// <summary>
    /// One participant's standing on the content board.
    /// </summary>
    /// <param name="Best">
    /// Their best single piece, and the only thing the board ranks on. The campaign is asking
    /// who made something good.
    /// </param>
    /// <param name="Judged">How many of their submissions a reviewer has marked.</param>
    /// <param name="Submitted">
    /// How many they sent in. Reported separately from <paramref name="Judged"/> because they
    /// are different facts: this used to be one field, counted from the judged rows and labelled
    /// "submissions", so somebody with nine pieces and one reviewed read as having made one.
    /// </param>
    public record Maker(string ProfileId, string Name, int Best, int Judged, int Submitted);

    /// <param name="AwaitingReview">
    /// How many submissions nobody has looked at. Reported rather than left out: a board of
    /// three names with thirty waiting reads as a campaign nobody entered.
    /// </param>
    public record Board(List<Maker> Makers, int AwaitingReview);

    /// <param name="Waiting">
    /// Everything nobody has marked, oldest first. Its own list, because the queue used to be
    /// the newest five hundred submissions reordered - so once five hundred judged submissions
    /// existed, unjudged work older than those was unreachable at any limit while the board went
    /// on announcing it as awaiting review.
    /// </param>
    public record Queue(List<ContentSubmission> Waiting, List<ContentSubmission> RecentlyJudged);

    // ── submitting ───────────────────────────────────────────────────────────

    public async Task<(SubmitResult Result, ContentSubmission? Submission)> SubmitAsync(
        UserProfile profile, string link, string platform, string note, CancellationToken ct = default)
    {
        var clean = SubmittedLink.Clean(link);
        if (clean is null) return (SubmitResult.LinkNotUsable, null);

        var submission = new ContentSubmission
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name.Trim(),
            Link = clean,
            LinkKey = SubmittedLink.KeyFor(clean),
            Platform = platform.Trim(),
            Note = note.Trim(),
        };

        try
        {
            await _db.ContentSubmissions.InsertOneAsync(submission, cancellationToken: ct);
        }
        catch (MongoWriteException failure) when (failure.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique index on (ProfileId, LinkKey) caught it. Said plainly rather than
            // accepted: five copies of one video was free, and it used to raise a standing.
            return (SubmitResult.AlreadySubmitted, null);
        }

        return (SubmitResult.Accepted, submission);
    }

    public async Task<List<ContentSubmission>> MineAsync(string profileId, CancellationToken ct = default) =>
        await _db.ContentSubmissions
            .Find(c => c.ProfileId == profileId)
            .SortByDescending(c => c.SubmittedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Takes back something nobody has marked yet.
    /// </summary>
    /// <remarks>
    /// There was no way to undo a mistaken submission - a wrong link stayed in the queue and
    /// then on the board for good. Only while it is unjudged: deleting a marked submission would
    /// delete a reviewer's record of having marked it.
    /// </remarks>
    public async Task<WithdrawResult> WithdrawAsync(string submissionId, string profileId, CancellationToken ct = default)
    {
        var deleted = await _db.ContentSubmissions.DeleteOneAsync(
            Builders<ContentSubmission>.Filter.And(
                Builders<ContentSubmission>.Filter.Eq(c => c.Id, submissionId),
                Builders<ContentSubmission>.Filter.Eq(c => c.ProfileId, profileId),
                Builders<ContentSubmission>.Filter.Eq(c => c.Score, null)),
            ct);

        if (deleted.DeletedCount > 0) return WithdrawResult.Withdrawn;

        // Which of the two it was. "Not found" for something the caller can see on their own
        // screen reads as a bug in Radar rather than as a rule.
        var exists = await _db.ContentSubmissions
            .CountDocumentsAsync(c => c.Id == submissionId && c.ProfileId == profileId, cancellationToken: ct);

        return exists > 0 ? WithdrawResult.AlreadyJudged : WithdrawResult.NotFound;
    }

    // ── reviewing ────────────────────────────────────────────────────────────

    /// <summary>
    /// The review queue, and separately the tail of what has been marked.
    /// </summary>
    /// <remarks>
    /// Two queries, two lists, because they answer different questions and one limit cannot
    /// serve both. Waiting work is oldest first - that is what a queue means, and the reviewer
    /// opening this screen is here to look at what has waited longest.
    /// </remarks>
    public async Task<Queue> QueueAsync(int limit = 200, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        var waiting = await _db.ContentSubmissions
            .Find(Builders<ContentSubmission>.Filter.Eq(c => c.Score, null))
            .SortBy(c => c.SubmittedAt)
            .Limit(limit)
            .ToListAsync(ct);

        var recentlyJudged = await _db.ContentSubmissions
            .Find(Builders<ContentSubmission>.Filter.Ne(c => c.Score, null))
            .SortByDescending(c => c.JudgedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return new Queue(waiting, recentlyJudged);
    }

    /// <summary>Records a reviewer's mark, keeping any mark it replaces.</summary>
    public async Task<JudgeResult> JudgeAsync(string submissionId, string reviewerProfileId, Judgement judgement, CancellationToken ct = default)
    {
        // Checked here as well as by the endpoint's attribute, because the attribute only
        // protects the one caller that goes through it, and a zero stored as a score would
        // satisfy every "has this been looked at" test in the system.
        if (judgement.Score < 1 || judgement.Score > 10) return JudgeResult.ScoreOutOfRange;

        var mark = new ContentSubmission.Mark
        {
            Score = judgement.Score,
            Verdict = judgement.Verdict.Trim(),
            ByProfileId = reviewerProfileId,
            At = DateTime.UtcNow,
        };

        // Own work excluded in the filter rather than after a read, so two requests racing
        // cannot slip between the check and the write.
        var result = await _db.ContentSubmissions.UpdateOneAsync(
            Builders<ContentSubmission>.Filter.And(
                Builders<ContentSubmission>.Filter.Eq(c => c.Id, submissionId),
                Builders<ContentSubmission>.Filter.Ne(c => c.ProfileId, reviewerProfileId)),
            Builders<ContentSubmission>.Update
                .Set(c => c.Score, mark.Score)
                .Set(c => c.Verdict, mark.Verdict)
                .Set(c => c.JudgedByProfileId, mark.ByProfileId)
                .Set(c => c.JudgedAt, mark.At)
                .Push(c => c.Marks, mark),
            cancellationToken: ct);

        if (result.MatchedCount > 0) return JudgeResult.Recorded;

        // Nothing matched, so either it is not there or it belongs to the reviewer. Told apart,
        // because "not found" for a submission sitting on the reviewer's own screen is the kind
        // of answer that sends somebody looking for a bug.
        var itsTheirs = await _db.ContentSubmissions
            .CountDocumentsAsync(c => c.Id == submissionId && c.ProfileId == reviewerProfileId, cancellationToken: ct);

        return itsTheirs > 0 ? JudgeResult.CannotJudgeOwnWork : JudgeResult.NotFound;
    }

    /// <summary>
    /// Who made what, and what a reviewer thought of it.
    /// </summary>
    /// <remarks>
    /// Ranked on the best single piece and on nothing else. Every tiebreak that was tried
    /// rewarded volume in some form - the first version summed the scores, so the same video
    /// submitted five times and marked nine each time beat one video marked nine - and the
    /// campaign's whole premise is that one good piece is the thing being asked for. Equal best
    /// marks are left equal, listed by name, for whoever runs the campaign to look at. That
    /// judgement is theirs; it is not a weighting to bury here.
    ///
    /// One pass over the collection, grouping in the database. It used to read every judged
    /// submission into managed memory unprojected - 464 MB of allocation and three seconds at
    /// sixty thousand judged, both linear - to run a LINQ group that took forty milliseconds.
    /// The same pass gives the submitted and judged counts and the awaiting total, so the
    /// figures on one board cannot contradict each other the way two round trips allowed.
    /// </remarks>
    public async Task<Board> BoardAsync(int limit = 50, CancellationToken ct = default)
    {
        var rows = await _db.ContentSubmissions.Aggregate()
            .Group(new BsonDocument
            {
                ["_id"] = "$ProfileId",

                // $max over the names rather than an arbitrary one: a blank sorts below any
                // real string, so a participant who submitted once before answering their name
                // and once after is still named on the board.
                ["name"] = new BsonDocument("$max", "$ProfileName"),

                // $max ignores missing and null, so this is the best mark that stands and not a
                // comparison against "not looked at yet".
                ["best"] = new BsonDocument("$max", "$Score"),

                // $isNumber, and deliberately not a comparison against null. Inside an
                // aggregation expression a missing field is "missing", which is not equal to
                // null - the opposite of the query language, where {Score: null} matches a
                // missing field. So $ne against null counted every unjudged submission as
                // judged, and this method reported nine submitted and nine marked when one had
                // been marked. $isNumber is true only for a score that is really there, which
                // also covers the legacy rows holding an explicit null.
                ["judged"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$isNumber", "$Score"),
                    1, 0,
                })),
                ["submitted"] = new BsonDocument("$sum", 1),
            })
            .ToListAsync(ct);

        var makers = rows
            .Where(row => row["best"].IsInt32 || row["best"].IsInt64)
            .Select(row => new Maker(
                row["_id"].IsString ? row["_id"].AsString : string.Empty,
                Named(row["name"]),
                row["best"].ToInt32(),
                row["judged"].ToInt32(),
                row["submitted"].ToInt32()))
            .OrderByDescending(maker => maker.Best)
            .ThenBy(maker => maker.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();

        var awaiting = rows.Sum(row => row["submitted"].ToInt32() - row["judged"].ToInt32());

        return new Board(makers, awaiting);
    }

    /// <summary>
    /// A name, or a word saying there isn't one.
    /// </summary>
    /// <remarks>
    /// A profile's name starts empty and stays empty until the person answers, which is this
    /// product's own rule - so a blank row on a prize-bearing board is the ordinary case, not a
    /// corner. Said rather than rendered as nothing.
    /// </remarks>
    private static string Named(BsonValue stored) =>
        stored.IsString && stored.AsString.Trim().Length > 0
            ? stored.AsString.Trim()
            : "Unnamed participant";
}

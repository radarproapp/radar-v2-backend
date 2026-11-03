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

    /// <summary>A participant's mark on one submission, and the reviewer's reason for it.</summary>
    public record Judgement(int Score, string Verdict);

    /// <summary>One participant's standing on the content board.</summary>
    /// <param name="Best">
    /// Their best single piece. The headline number, because the campaign is asking who made
    /// something good - not who uploaded the most links, which totalling would reward.
    /// </param>
    public record Maker(string ProfileId, string Name, int Submissions, int Best, int Total);

    /// <param name="AwaitingReview">
    /// How many submissions nobody has looked at. Reported rather than left out: a board of
    /// three names with thirty in the queue reads as a campaign nobody entered.
    /// </param>
    public record Board(List<Maker> Makers, long AwaitingReview);

    public async Task<ContentSubmission> SubmitAsync(UserProfile profile, string link, string platform, string note, CancellationToken ct = default)
    {
        var submission = new ContentSubmission
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            Link = link.Trim(),
            Platform = platform.Trim(),
            Note = note.Trim(),
        };

        await _db.ContentSubmissions.InsertOneAsync(submission, cancellationToken: ct);
        return submission;
    }

    public async Task<List<ContentSubmission>> MineAsync(string profileId, CancellationToken ct = default) =>
        await _db.ContentSubmissions
            .Find(c => c.ProfileId == profileId)
            .SortByDescending(c => c.SubmittedAt)
            .ToListAsync(ct);

    /// <summary>Everything submitted, unjudged first.</summary>
    /// <remarks>
    /// Unjudged first because that is the queue. A reviewer opening this screen is here to look
    /// at what nobody has looked at, not to re-read what they already marked.
    /// </remarks>
    public async Task<List<ContentSubmission>> QueueAsync(int limit = 200, CancellationToken ct = default)
    {
        var all = await _db.ContentSubmissions
            .Find(FilterDefinition<ContentSubmission>.Empty)
            .SortByDescending(c => c.SubmittedAt)
            .Limit(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        return all
            .OrderBy(c => c.IsJudged)
            .ThenByDescending(c => c.SubmittedAt)
            .ToList();
    }

    /// <summary>Records a reviewer's mark. False when there is no such submission.</summary>
    public async Task<bool> JudgeAsync(string submissionId, string reviewerProfileId, Judgement judgement, CancellationToken ct = default)
    {
        var result = await _db.ContentSubmissions.UpdateOneAsync(
            c => c.Id == submissionId,
            Builders<ContentSubmission>.Update
                .Set(c => c.Score, judgement.Score)
                .Set(c => c.Verdict, judgement.Verdict.Trim())
                .Set(c => c.JudgedByProfileId, reviewerProfileId)
                .Set(c => c.JudgedAt, DateTime.UtcNow),
            cancellationToken: ct);

        return result.MatchedCount > 0;
    }

    /// <summary>
    /// Who made what, and what a reviewer thought of it.
    /// </summary>
    /// <remarks>
    /// Judged submissions only. An unjudged one has not scored badly - it has not been looked at
    /// - and ranking the two together tells a participant their work was rejected when it is
    /// sitting in a queue.
    /// </remarks>
    public async Task<Board> BoardAsync(int limit = 50, CancellationToken ct = default)
    {
        // Score is written by [BsonIgnoreIfNull], so an unjudged submission has no Score element
        // at all rather than an explicit null. $ne: null excludes both shapes, so this stays
        // right for any row written before that attribute existed.
        var judged = await _db.ContentSubmissions
            .Find(Builders<ContentSubmission>.Filter.Ne(c => c.Score, null))
            .ToListAsync(ct);

        var awaiting = await _db.ContentSubmissions
            .CountDocumentsAsync(Builders<ContentSubmission>.Filter.Eq(c => c.Score, null), cancellationToken: ct);

        var makers = judged
            .GroupBy(c => c.ProfileId)
            .Select(group => new Maker(
                group.Key,
                group.OrderByDescending(c => c.SubmittedAt).First().ProfileName,
                group.Count(),
                group.Max(c => c.Score!.Value),
                group.Sum(c => c.Score!.Value)))
            .OrderByDescending(maker => maker.Best)
            .ThenByDescending(maker => maker.Total)
            .ThenBy(maker => maker.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();

        return new Board(makers, awaiting);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// The half of the campaign Radar cannot measure.
/// </summary>
/// <remarks>
/// Two things are being rewarded and they are counted entirely separately: bringing people in,
/// which Radar can see and which lives in <see cref="ReferralsController"/>, and making
/// something that explains Radar, which lives on somebody else's platform and which Radar
/// cannot see at all.
///
/// The two boards are never averaged. A combined score would have to decide what one video is
/// worth in users, and there is no honest exchange rate - the question "should an excellent
/// video beat thirty real users" is a judgement for whoever runs the campaign, made in front of
/// both numbers, not a weighting buried in a service.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/campaign")]
public class CampaignController : ControllerBase
{
    private readonly ContentSubmissionService _content;
    private readonly IUserProfileService _profiles;

    public CampaignController(ContentSubmissionService content, IUserProfileService profiles)
    {
        _content = content;
        _profiles = profiles;
    }

    public sealed class SubmitRequest
    {
        /// <summary>
        /// Where the thing is.
        /// </summary>
        /// <remarks>
        /// Length only. <c>[Url]</c> used to be here and three comments called it a url check;
        /// it is a scheme-prefix test that accepts a bare "https://", a newline mid-path and
        /// markup in the path, while rejecting a link with a leading space that the service was
        /// about to trim one line later. <see cref="SubmittedLink"/> does the real check, after
        /// the trim.
        /// </remarks>
        [Required, StringLength(500, MinimumLength = 8)]
        public string Link { get; set; } = string.Empty;

        [Required, StringLength(40, MinimumLength = 2)]
        public string Platform { get; set; } = string.Empty;

        [StringLength(1000)]
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Submits something the participant made.
    /// </summary>
    /// <remarks>
    /// Radar does not fetch the link: following a link a stranger submitted, from the server, is
    /// a request Radar's own address makes on somebody else's behalf, and the only thing it
    /// would buy is confirming the page exists - which the human reviewer does anyway.
    /// </remarks>
    [HttpPost("submissions")]
    public async Task<IActionResult> SubmitAsync([FromBody] SubmitRequest request, CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var (result, submission) = await _content.SubmitAsync(profile, request.Link, request.Platform, request.Note, ct);

        return result switch
        {
            ContentSubmissionService.SubmitResult.LinkNotUsable => BadRequest(new
            {
                error = "That does not look like a link anybody can open. Paste the full address, starting with https://",
            }),
            ContentSubmissionService.SubmitResult.AlreadySubmitted => Conflict(new
            {
                error = "You have already submitted this link.",
            }),
            _ => Ok(Describe(submission!, forReviewer: false)),
        };
    }

    /// <summary>Everything this participant has submitted, and what came of it.</summary>
    [HttpGet("submissions/mine")]
    public async Task<IActionResult> MineAsync(CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var mine = await _content.MineAsync(profile.Id, ct);
        return Ok(mine.Select(submission => Describe(submission, forReviewer: false)));
    }

    /// <summary>Takes back a submission nobody has marked yet.</summary>
    [HttpDelete("submissions/{id}")]
    public async Task<IActionResult> WithdrawAsync(string id, CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return await _content.WithdrawAsync(id, profile.Id, ct) switch
        {
            ContentSubmissionService.WithdrawResult.Withdrawn => NoContent(),
            ContentSubmissionService.WithdrawResult.AlreadyJudged => Conflict(new
            {
                error = "Somebody has already reviewed this, so it cannot be taken back.",
            }),
            _ => NotFound(new { error = "Submission not found" }),
        };
    }

    // ── reviewing ────────────────────────────────────────────────────────────

    /// <summary>
    /// The review queue, and separately the tail of what has been marked.
    /// </summary>
    /// <remarks>
    /// Two lists rather than one sorted one. This was the newest five hundred submissions
    /// reordered to put unjudged first, so once five hundred judged submissions existed, older
    /// unjudged work was unreachable at every value of the limit - while the board went on
    /// reporting it as awaiting review.
    /// </remarks>
    [Authorize(Roles = "SuperAdmin,PlatformAdmin")]
    [HttpGet("submissions")]
    public async Task<IActionResult> QueueAsync([FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var queue = await _content.QueueAsync(limit, ct);

        return Ok(new
        {
            waiting = queue.Waiting.Select(submission => Describe(submission, forReviewer: true)),
            recentlyJudged = queue.RecentlyJudged.Select(submission => Describe(submission, forReviewer: true)),
        });
    }

    public sealed class JudgeRequest
    {
        [Range(1, 10)] public int Score { get; set; }

        /// <summary>
        /// Why it got that mark.
        /// </summary>
        /// <remarks>
        /// Required. A mark with no reason is a number a participant cannot argue with, which
        /// is the thing this product refuses to produce anywhere else and should not start
        /// producing where a prize is at stake.
        /// </remarks>
        [Required, StringLength(2000, MinimumLength = 10)]
        public string Verdict { get; set; } = string.Empty;
    }

    /// <summary>
    /// Records a reviewer's mark and their reason for it.
    /// </summary>
    /// <remarks>
    /// Not on their own work. An admin is a participant too - submitting only asks that a caller
    /// be signed in - so an admin could post a link and award it a ten, and the one field
    /// recording who did it was never read back by anything.
    ///
    /// A mark that replaces an earlier one does not erase it. Both are kept and both are
    /// returned to a reviewer, because a number that decides a prize has to stay arguable.
    /// </remarks>
    [Authorize(Roles = "SuperAdmin,PlatformAdmin")]
    [HttpPost("submissions/{id}/judge")]
    public async Task<IActionResult> JudgeAsync(string id, [FromBody] JudgeRequest request, CancellationToken ct = default)
    {
        // The account id from the token, not a profile. An admin login is a separate account
        // with no profile at all - it exists to run Radar, not to read it - so asking for one
        // here would make judging impossible for exactly the people who do the judging.
        var reviewer = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(reviewer)) return Unauthorized(new { error = "Not signed in" });

        var judgement = new ContentSubmissionService.Judgement(request.Score, request.Verdict);

        return await _content.JudgeAsync(id, reviewer, judgement, ct) switch
        {
            ContentSubmissionService.JudgeResult.Recorded => NoContent(),
            ContentSubmissionService.JudgeResult.CannotJudgeOwnWork => StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "You cannot mark your own submission. Ask another reviewer to look at it.",
            }),
            ContentSubmissionService.JudgeResult.ScoreOutOfRange => BadRequest(new
            {
                error = "A mark is between 1 and 10.",
            }),
            _ => NotFound(new { error = "Submission not found" }),
        };
    }

    /// <summary>
    /// The content board, reported beside the acquisition board and never combined with it.
    /// </summary>
    [Authorize(Roles = "SuperAdmin,PlatformAdmin")]
    [HttpGet("board")]
    public async Task<IActionResult> BoardAsync([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var board = await _content.BoardAsync(limit, ct);

        return Ok(new
        {
            makers = board.Makers,
            awaitingReview = board.AwaitingReview,

            // Said on the board rather than left for a reader to infer, because every tiebreak
            // that was tried rewarded volume in some form and somebody will ask why there isn't
            // one.
            rankedOn = "The best single piece. Equal best marks are left equal.",
        });
    }

    /// <param name="forReviewer">
    /// Whether the caller may see who marked it. A participant gets the mark and the reason; the
    /// reviewer's identity is for the admin view, where accountability is the point.
    /// </param>
    private static object Describe(ContentSubmission submission, bool forReviewer) => new
    {
        submission.Id,
        submission.ProfileId,

        // Said rather than rendered blank. A name starts empty and stays empty until the person
        // answers, so an unnamed row is the ordinary case, not a corner.
        profileName = submission.ProfileName.Trim().Length > 0 ? submission.ProfileName.Trim() : "Unnamed participant",

        submission.Link,
        submission.Platform,
        submission.Note,
        submission.SubmittedAt,
        submission.Score,
        submission.Verdict,
        submission.JudgedAt,

        // Explicit, so a client never has to decide what a null score means. Null is "nobody
        // has looked yet", which is not the same as a low mark.
        isJudged = submission.IsJudged,

        judgedBy = forReviewer ? submission.JudgedByProfileId : null,

        // Every mark it has carried, so one that replaced another is visible rather than gone.
        marks = submission.Marks.Select(mark => new
        {
            mark.Score,
            mark.Verdict,
            byProfileId = forReviewer ? mark.ByProfileId : null,
            mark.At,
        }),
    };
}

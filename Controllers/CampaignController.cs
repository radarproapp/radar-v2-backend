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
        [Required, Url, StringLength(500, MinimumLength = 8)]
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
    /// The link is checked for being a URL and for nothing else. Radar does not fetch it:
    /// following a link a stranger submitted, from the server, is a request Radar's own address
    /// makes on somebody else's behalf, and the only thing it would buy is confirming the page
    /// exists - which the human reviewer does anyway.
    /// </remarks>
    [HttpPost("submissions")]
    public async Task<IActionResult> SubmitAsync([FromBody] SubmitRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var submission = await _content.SubmitAsync(profile, request.Link, request.Platform, request.Note, ct);
        return Ok(Describe(submission));
    }

    /// <summary>Everything this participant has submitted, and what came of it.</summary>
    [HttpGet("submissions/mine")]
    public async Task<IActionResult> MineAsync(CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var mine = await _content.MineAsync(profile.Id, ct);
        return Ok(mine.Select(Describe));
    }

    // ── reviewing ────────────────────────────────────────────────────────────

    /// <summary>The review queue: everything submitted, unjudged first.</summary>
    [Authorize(Roles = "SuperAdmin,PlatformAdmin")]
    [HttpGet("submissions")]
    public async Task<IActionResult> QueueAsync([FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var queue = await _content.QueueAsync(limit, ct);
        return Ok(queue.Select(Describe));
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

    /// <summary>Records a reviewer's mark and their reason for it.</summary>
    [Authorize(Roles = "SuperAdmin,PlatformAdmin")]
    [HttpPost("submissions/{id}/judge")]
    public async Task<IActionResult> JudgeAsync(string id, [FromBody] JudgeRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var reviewer = await _profiles.GetCurrentUserAsync();
        if (reviewer is null) return NotFound(new { error = "Profile not found" });

        var judged = await _content.JudgeAsync(id, reviewer.Id, new ContentSubmissionService.Judgement(request.Score, request.Verdict), ct);

        return judged ? NoContent() : NotFound(new { error = "Submission not found" });
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
        });
    }

    private static object Describe(ContentSubmission submission) => new
    {
        submission.Id,
        submission.ProfileId,
        submission.ProfileName,
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
    };
}

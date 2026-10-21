using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// What Radar knows about this user's learning, in one place.
///
/// Exposed deliberately: personalisation that the user cannot see or correct is indistinguishable
/// from personalisation that is not happening, which is exactly how the old hardcoded screens felt.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/learning-record")]
public class LearningRecordController : ControllerBase
{
    private readonly IUserLearningRecordService _records;
    private readonly IUserProfileService _profiles;
    private readonly IBehavioralSignalService _signals;

    public LearningRecordController(
        IUserLearningRecordService records,
        IUserProfileService profiles,
        IBehavioralSignalService signals)
    {
        _records = records;
        _profiles = profiles;
        _signals = signals;
    }

    public sealed class CorrectionRequest
    {
        /// <summary>The term Radar inferred that the user says is not about them.</summary>
        public string Term { get; set; } = string.Empty;
    }

    /// <summary>
    /// "This is not about me." Records the user's disagreement with something Radar inferred.
    ///
    /// Everything in the record except the profile fields is an inference drawn from behaviour —
    /// a term is there because of what the user opened, saved or finished, which is a guess about
    /// why they did it. Reading a single article about something is enough to put it in front of
    /// them again, and a guess the user cannot argue with is just a wrong answer they have to live
    /// with.
    ///
    /// Stored as a Declared signal rather than deleted: a deletion would be re-inferred by the
    /// next thing they click, whereas a declared correction outranks inference and survives it.
    /// </summary>
    [HttpPost("corrections")]
    public async Task<IActionResult> CorrectAsync([FromBody] CorrectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Term))
        {
            return BadRequest(new { error = "Tell Radar which term is wrong." });
        }

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _signals.RecordAsync(profile.Id, request.Term.Trim(), BehavioralAction.RemoveInterest, SignalSource.Declared);

        // Returns the rebuilt record so the caller can show the corrected version immediately
        // rather than guessing at what changed.
        return Ok(await _records.BuildAsync(profile, ct));
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _records.BuildAsync(profile, ct));
    }
}

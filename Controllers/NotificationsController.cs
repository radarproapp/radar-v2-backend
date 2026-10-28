using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Turning messages off.
/// </summary>
/// <remarks>
/// Anonymous on purpose. An unsubscribe link has to work for somebody who is not signed in, on a
/// device that has never been signed in, months after the message arrived — asking them to log in
/// first is how an opt-out becomes a spam report, and the Privacy Policy commits to a working
/// mechanism in section 3.8.
/// </remarks>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly RadarDatabase _db;
    private readonly IUserProfileService _profiles;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationsController> _log;

    public NotificationsController(
        RadarDatabase db,
        IUserProfileService profiles,
        IConfiguration config,
        ILogger<NotificationsController> log)
    {
        _db = db;
        _profiles = profiles;
        _config = config;
        _log = log;
    }

    public sealed class UnsubscribeRequest
    {
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>
    /// Switches off whichever stream the token names.
    /// </summary>
    /// <remarks>
    /// The token is a signature over the profile id and the kind, so it cannot be altered to
    /// unsubscribe somebody else, and the kind being inside the signature means a link that stops
    /// the daily update cannot be made to stop a password reset.
    ///
    /// Reports success for an invalid token too. Somebody who has already unsubscribed, or who
    /// clicked an old link, should be told it is off rather than handed an error that invites
    /// them to report the message instead — and a different answer for a valid token would make
    /// this an oracle for which profile ids exist.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> UnsubscribeAsync(
        [FromBody] UnsubscribeRequest request, CancellationToken ct = default)
    {
        var signingKey = _config["Auth:Jwt:Key"] ?? string.Empty;
        var read = UnsubscribeToken.Read(request.Token, signingKey);

        if (read is null)
        {
            _log.LogInformation("An unsubscribe link did not verify.");
            return Ok(new { message = "You will not get that message again." });
        }

        var (profileId, kind) = read.Value;

        var update = kind switch
        {
            UnsubscribeToken.DailyDigest =>
                Builders<Models.UserProfile>.Update.Set(p => p.Notifications.DailyDigest, false),
            _ => null,
        };

        if (update is null) return Ok(new { message = "You will not get that message again." });

        await _db.Profiles.UpdateOneAsync(p => p.Id == profileId, update, cancellationToken: ct);

        return Ok(new { message = "You will not get that message again." });
    }

    /// <summary>What this profile is signed up to, for the settings screen.</summary>
    [Authorize]
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferencesAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(profile.Notifications);
    }

    /// <summary>Changes them.</summary>
    [Authorize]
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferencesAsync(
        [FromBody] Models.NotificationPrefs prefs, CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        profile.Notifications = prefs;
        await _profiles.SaveProfileAsync(profile);

        return Ok(profile.Notifications);
    }
}

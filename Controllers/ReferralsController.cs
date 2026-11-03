using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// A participant's referral link, and what it has actually brought in.
/// </summary>
/// <remarks>
/// Two things are being rewarded and they are not the same: making something that explains
/// Radar, and getting people onto it. This is the half that can be counted. The other half -
/// videos on other people's platforms - cannot be measured from inside Radar at all, and
/// pretending otherwise would put a number on a screen that nothing produced.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/referrals")]
public class ReferralsController : ControllerBase
{
    private readonly ReferralService _referrals;
    private readonly IUserProfileService _profiles;
    private readonly IConfiguration _config;

    public ReferralsController(ReferralService referrals, IUserProfileService profiles, IConfiguration config)
    {
        _referrals = referrals;
        _profiles = profiles;
        _config = config;
    }

    /// <summary>
    /// My code, my link, and the three counts behind it.
    /// </summary>
    /// <remarks>
    /// The code is issued on the first request rather than at sign-up, so the collection is not
    /// filled with codes for accounts that will never share one.
    ///
    /// All three counts are returned, not a single score. A sign-up is not a user: somebody who
    /// gets thirty people to create an account and walk away has produced thirty rows, and a
    /// number that cannot tell that from thirty people who come back every week would reward
    /// exactly the behaviour nobody wants.
    /// </remarks>
    [HttpGet("mine")]
    public async Task<IActionResult> MineAsync(CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var code = await _referrals.CodeForAsync(profile.Id, ct);
        var brought = await _referrals.BroughtByAsync(profile.Id, ct);

        return Ok(new
        {
            code,
            link = $"{PublicSite.From(_config)}/join/{code}",
            signedUp = brought.SignedUp,
            personalised = brought.Personalised,
            cameBack = brought.CameBack,
        });
    }

    /// <summary>
    /// Who has brought people in, best first.
    /// </summary>
    /// <remarks>
    /// Admin-gated. A public board naming how many people each participant brought is a race
    /// somebody wins by buying sign-ups, and it tells every participant their standing against
    /// named rivals - which is a different product decision from running a campaign, and not
    /// one to make by leaving an endpoint open.
    ///
    /// Ordered by the people who came back, then those who personalised, then raw sign-ups. The
    /// ordering is the campaign's incentive: ranking on sign-ups would tell participants that
    /// thirty abandoned accounts beat five people who use Radar every week.
    /// </remarks>
    [Authorize(Roles = "SuperAdmin,PlatformAdmin")]
    [HttpGet("board")]
    public async Task<IActionResult> BoardAsync([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        return Ok(await _referrals.BoardAsync(limit, ct));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// The signed-in home screen, in one request. Replaces the three-way fan-out the old Today page
/// made, and the hand-written mock data the career dashboards shipped with.
/// </summary>
[ApiController]
[Authorize]
[Route("api/home")]
public class HomeController : ControllerBase
{
    private readonly IHomeBriefService _home;
    private readonly IUserProfileService _profiles;

    public HomeController(IHomeBriefService home, IUserProfileService profiles)
    {
        _home = home;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _home.BuildAsync(profile, ct));
    }
}

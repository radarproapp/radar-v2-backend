using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/today")]
public class TodayController : ControllerBase
{
    private readonly INavigatorService _navigator;
    private readonly IUserProfileService _profiles;

    public TodayController(INavigatorService navigator, IUserProfileService profiles)
    {
        _navigator = navigator;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var focus = await _navigator.GetTodaysFocusAsync(profile);
        return Ok(focus);
    }
}

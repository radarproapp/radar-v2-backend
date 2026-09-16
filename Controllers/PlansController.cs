using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/plans")]
public class PlansController : ControllerBase
{
    private readonly IPlansService _plans;
    private readonly IUserProfileService _profiles;

    public PlansController(IPlansService plans, IUserProfileService profiles)
    {
        _plans = plans;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync() => Ok(await _plans.GetPlansAsync());

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _plans.GetCurrentPlanAsync(profile.Id);
        return plan is null ? NoContent() : Ok(plan);
    }
}

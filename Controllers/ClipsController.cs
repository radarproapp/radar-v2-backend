using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/clips")]
public class ClipsController : ControllerBase
{
    private readonly IClipsService _clips;
    private readonly IUserProfileService _profiles;

    public ClipsController(IClipsService clips, IUserProfileService profiles)
    {
        _clips = clips;
        _profiles = profiles;
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetTodayAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _clips.GetDailyClipsAsync(profile));
    }

    [HttpPost("{id}/save")]
    public async Task<IActionResult> SaveAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _clips.SaveClipAsync(profile.Id, id);
        return NoContent();
    }
}

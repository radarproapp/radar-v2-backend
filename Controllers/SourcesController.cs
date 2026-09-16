using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/sources")]
public class SourcesController : ControllerBase
{
    private readonly ISourceService _sources;
    private readonly IUserProfileService _profiles;

    public SourcesController(ISourceService sources, IUserProfileService profiles)
    {
        _sources = sources;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _sources.GetSourcesAsync(profile.Id));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAsync(string id)
    {
        var source = await _sources.GetSourceAsync(id);
        return source is null
            ? NotFound(new { error = "Source not found" })
            : Ok(source);
    }

    [HttpPost("{id}/follow")]
    public async Task<IActionResult> ToggleFollowAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _sources.ToggleFollowAsync(profile.Id, id);
        return NoContent();
    }

    [HttpPost("{id}/weekly-brief")]
    public async Task<IActionResult> ToggleWeeklyBriefAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _sources.ToggleWeeklyBriefAsync(profile.Id, id);
        return NoContent();
    }

    [HttpPost("{id}/prioritise")]
    public async Task<IActionResult> TogglePrioritiseAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _sources.TogglePrioritiseAsync(profile.Id, id);
        return NoContent();
    }
}

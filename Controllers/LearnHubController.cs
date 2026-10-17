using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/learn-hub")]
public class LearnHubController : ControllerBase
{
    private readonly ILearnHubService _learnHub;
    private readonly IUserProfileService _profiles;

    public LearnHubController(ILearnHubService learnHub, IUserProfileService profiles)
    {
        _learnHub = learnHub;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] string? subInterest = null, [FromQuery] string? stage = null)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var payload = await _learnHub.GetLearnHubAsync(profile.Id, subInterest, stage);
        return Ok(payload);
    }

    [HttpPost("resources/{resourceId}/complete")]
    public async Task<IActionResult> CompleteResourceAsync(string resourceId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _learnHub.CompleteResourceAsync(profile.Id, resourceId);
        return NoContent();
    }

    [HttpPost("resources/{resourceId}/save")]
    public async Task<IActionResult> ToggleSaveAsync(string resourceId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _learnHub.ToggleSaveResourceAsync(profile.Id, resourceId);
        return NoContent();
    }

    [HttpPost("nodes/activity")]
    public async Task<IActionResult> RecordNodeActivityAsync([FromBody] NodeActivityRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _learnHub.RecordNodeActivityAsync(profile.Id, request);
        return NoContent();
    }
}

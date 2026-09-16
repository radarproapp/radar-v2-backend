using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/opportunities")]
public class OpportunitiesController : ControllerBase
{
    private readonly IOpportunityService _opportunities;
    private readonly IUserProfileService _profiles;

    public OpportunitiesController(IOpportunityService opportunities, IUserProfileService profiles)
    {
        _opportunities = opportunities;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var items = await _opportunities.GetOpportunitiesAsync(profile, ParseType(type), Math.Max(1, page), Math.Clamp(pageSize, 1, 50));
        return Ok(items);
    }

    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _opportunities.GetSavedOpportunitiesAsync(profile.Id));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(string id)
    {
        var opp = await _opportunities.GetByIdAsync(id);
        return opp is null ? NotFound(new { error = "Opportunity not found" }) : Ok(opp);
    }

    [HttpPost("{id}/save")]
    public async Task<IActionResult> SaveAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();

        await _opportunities.SaveOpportunityAsync(profile.Id, id);
        return NoContent();
    }

    [HttpDelete("{id}/save")]
    public async Task<IActionResult> UnsaveAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();

        await _opportunities.UnsaveOpportunityAsync(profile.Id, id);
        return NoContent();
    }

    [HttpPost("{id}/applied")]
    public async Task<IActionResult> MarkAppliedAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();

        await _opportunities.MarkAppliedAsync(profile.Id, id);
        return NoContent();
    }

    private static OpportunityType? ParseType(string? type) =>
        Enum.TryParse<OpportunityType>(type, ignoreCase: true, out var parsed) ? parsed : null;
}

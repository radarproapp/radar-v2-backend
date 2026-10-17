using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Helpers;
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

    private static readonly Dictionary<string, Func<Opportunity, object?>> OpportunityFieldProjectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = o => o.Id,
        ["type"] = o => o.Type,
        ["title"] = o => o.Title,
        ["organisation"] = o => o.Organisation,
        ["logoUrl"] = o => o.LogoUrl,
        ["description"] = o => o.Description,
        ["deadline"] = o => o.Deadline,
        ["requirements"] = o => o.Requirements,
        ["matchScorePercent"] = o => o.MatchScorePercent,
        ["matchConfidence"] = o => o.MatchConfidence,
        ["whyItFits"] = o => o.WhyItFits,
        ["preparationSteps"] = o => o.PreparationSteps,
        ["url"] = o => o.Url,
        ["location"] = o => o.Location,
        ["isRemote"] = o => o.IsRemote,
        ["isSaved"] = o => o.IsSaved,
        ["regionTag"] = o => o.RegionTag,
        ["goalTag"] = o => o.GoalTag,
        ["benefits"] = o => o.Benefits,
        ["cost"] = o => o.Cost,
        ["radarsAdvice"] = o => o.RadarsAdvice,
        ["nextMoveSteps"] = o => o.NextMoveSteps,
        ["status"] = o => o.Status,
        ["userTrackingStatus"] = o => o.UserTrackingStatus,
        ["reminderSet"] = o => o.ReminderSet,
        ["matchTier"] = o => o.MatchTier,
        ["urgencyLabel"] = o => o.UrgencyLabel,
    };

    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? type = null,
        [FromQuery] string? region = null,
        [FromQuery] string? goal = null,
        [FromQuery] string? urgency = null,
        [FromQuery] string? matchTier = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? fields = null)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        if (!FieldSet.TryParse(fields, OpportunityFieldProjectors.Keys.ToList(), out var fieldSet, out var fieldError))
            return BadRequest(new { error = fieldError });

        var items = await _opportunities.GetOpportunitiesAsync(
            profile,
            ParseType(type),
            region,
            goal,
            urgency,
            matchTier,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 50));

        return Ok(fieldSet.IsEmpty ? items : items.Select(item => fieldSet.Project(item, OpportunityFieldProjectors)));
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var feed = await _opportunities.GetDailyFeedAsync(profile);
        return Ok(feed);
    }

    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _opportunities.GetSavedOpportunitiesAsync(profile.Id));
    }

    [HttpGet("pipeline")]
    public async Task<IActionResult> GetPipelineAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _opportunities.GetTrackedPipelineAsync(profile.Id));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        var opp = await _opportunities.GetByIdAsync(id, profile);
        return opp is null ? NotFound(new { error = "Opportunity not found" }) : Ok(opp);
    }

    [HttpPost("{id}/status")]
    public async Task<IActionResult> UpdateStatusAsync(string id, [FromBody] OpportunityStatusUpdateRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();

        await _opportunities.UpdateStatusAsync(profile.Id, id, request.Status, request.Notes);
        return NoContent();
    }

    [HttpPost("{id}/reminder")]
    public async Task<IActionResult> ToggleReminderAsync(string id, [FromBody] OpportunityReminderRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();

        await _opportunities.ToggleReminderAsync(profile.Id, id, request.ReminderSet);
        return NoContent();
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

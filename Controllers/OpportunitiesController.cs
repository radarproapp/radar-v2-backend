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

    /// <summary>Fields a caller may request via <c>?fields=</c> — see FeedController for why this is
    /// an explicit whitelist rather than reflection over the model.</summary>
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
    };

    /// <summary>
    /// Opportunities, offset-paginated. Left on page/pageSize rather than moved to a cursor: the set
    /// is a slowly-changing seed of a few dozen rows, so the shifting-window problem a cursor solves
    /// is not in play here. The sparseness win applies regardless, via <c>?fields=</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? fields = null)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        if (!FieldSet.TryParse(fields, OpportunityFieldProjectors.Keys.ToList(), out var fieldSet, out var fieldError))
            return BadRequest(new { error = fieldError });

        var items = await _opportunities.GetOpportunitiesAsync(profile, ParseType(type), Math.Max(1, page), Math.Clamp(pageSize, 1, 50));

        return Ok(fieldSet.IsEmpty ? items : items.Select(item => fieldSet.Project(item, OpportunityFieldProjectors)));
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

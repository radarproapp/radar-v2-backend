using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/roadmaps")]
public class RoadmapsController : ControllerBase
{
    private readonly IRoadmapService _roadmaps;
    private readonly IUserProfileService _profiles;

    public RoadmapsController(IRoadmapService roadmaps, IUserProfileService profiles)
    {
        _roadmaps = roadmaps;
        _profiles = profiles;
    }

    public sealed class AddTopicRequest
    {
        [Required, MinLength(1)] public string Topic { get; set; } = string.Empty;
    }

    [HttpPost("active/topics")]
    public async Task<IActionResult> AddTopicToActiveAsync([FromBody] AddTopicRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var active = await _roadmaps.GetActiveRoadmapAsync(profile.Id);
        if (active is null) return NotFound(new { error = "No active roadmap" });

        await _roadmaps.AddTopicToRoadmapAsync(profile.Id, active.Id, request.Topic.Trim());
        return NoContent();
    }
}

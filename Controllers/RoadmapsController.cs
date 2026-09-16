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

    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _roadmaps.GetUserRoadmapsAsync(profile.Id));
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // Mirrors Learn.razor: GetActiveRoadmapAsync auto-creates from the user's goal template
        // when no active roadmap exists yet, so this only 404s when the profile itself is missing.
        var active = await _roadmaps.GetActiveRoadmapAsync(profile.Id);
        return active is null ? NotFound(new { error = "No active roadmap" }) : Ok(active);
    }

    [HttpPost("{roadmapId}/modules/{moduleId}/lessons/{lessonId}/complete")]
    public async Task<IActionResult> CompleteLessonAsync(string roadmapId, string moduleId, string lessonId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _roadmaps.CompleteLessonAsync(profile.Id, roadmapId, moduleId, lessonId);
        return NoContent();
    }

    [HttpGet("{roadmapId}/progress")]
    public async Task<IActionResult> GetProgressAsync(string roadmapId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var percent = await _roadmaps.GetProgressPercentAsync(profile.Id, roadmapId);
        return Ok(new { progressPercent = percent });
    }
}

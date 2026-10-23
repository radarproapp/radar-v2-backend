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

    public sealed class SubmitQuizRequest
    {
        [Required] public string Topic { get; set; } = string.Empty;
        [Required] public string Stage { get; set; } = "Beginner";
        [Range(0, 100)] public int ScorePercent { get; set; }
    }

    public sealed class SetStageRequest
    {
        [Required] public string Stage { get; set; } = "Beginner";
    }

    public sealed class AddSectionRequest
    {
        [Required, MinLength(1)] public string Subject { get; set; } = string.Empty;
        [Required] public string Level { get; set; } = "Beginner";

        /// <summary>What they want to be able to do. Optional — asked, but skippable.</summary>
        public string? Outcome { get; set; }
    }

    /// <summary>
    /// Adds one section to the learner's path, from what they told us they want.
    /// </summary>
    /// <remarks>
    /// Separate from <c>generate</c> on purpose. <c>generate</c> reads the profile and returns a
    /// whole four-stage pathway, which is how a Computer Science student ended up with
    /// "Foundations of Computer Science" and "Core concepts and vocabulary in Computer Science"
    /// without being asked a single question. This endpoint only ever adds what was asked for.
    /// </remarks>
    [HttpPost("sections")]
    public async Task<IActionResult> AddSectionAsync([FromBody] AddSectionRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var roadmap = await _roadmaps.AddSectionAsync(
            profile.Id,
            profile,
            request.Subject.Trim(),
            request.Level.Trim(),
            string.IsNullOrWhiteSpace(request.Outcome) ? null : request.Outcome.Trim(),
            ct);

        return Ok(roadmap);
    }

    [HttpPost("active/topics")]
    public async Task<IActionResult> AddTopicToActiveAsync([FromBody] AddTopicRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // No 404 for a missing path. Paths start empty now, so the first "add to my path" a
        // learner ever clicks would otherwise be refused.
        var active = await _roadmaps.GetActiveRoadmapAsync(profile.Id);
        await _roadmaps.AddTopicToRoadmapAsync(profile.Id, active?.Id ?? string.Empty, request.Topic.Trim());
        return NoContent();
    }

    /// <summary>
    /// Replaces the user's active roadmap with an AI-built learning pathway tailored to their
    /// profile and stated level. Falls back to the deterministic template pathway when the AI
    /// engine is unavailable, so this always returns a usable roadmap.
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _roadmaps.CreateAiRoadmapAsync(profile.Id, profile));
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

    [HttpPost("{roadmapId}/resources/{resourceId}/complete")]
    public async Task<IActionResult> CompleteResourceAsync(string roadmapId, string resourceId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _roadmaps.CompleteResourceAsync(profile.Id, roadmapId, resourceId);
        return NoContent();
    }

    [HttpPost("{roadmapId}/exercises/{resourceId}/complete")]
    public async Task<IActionResult> CompleteExerciseAsync(string roadmapId, string resourceId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _roadmaps.CompleteExerciseAsync(profile.Id, roadmapId, resourceId);
        return NoContent();
    }

    [HttpPost("{roadmapId}/quizzes")]
    public async Task<IActionResult> RecordQuizResultAsync(string roadmapId, [FromBody] SubmitQuizRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var updated = await _roadmaps.RecordQuizResultAsync(profile.Id, roadmapId, request.Topic, request.Stage, request.ScorePercent);
        return updated is null ? NotFound(new { error = "Roadmap not found" }) : Ok(updated);
    }

    [HttpPost("{roadmapId}/stage")]
    public async Task<IActionResult> SetStageAsync(string roadmapId, [FromBody] SetStageRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _roadmaps.SetRoadmapStageAsync(profile.Id, roadmapId, request.Stage);
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

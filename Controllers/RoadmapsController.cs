using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Helpers;
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

    /// <summary>
    /// Builds a section from a document the learner uploaded.
    /// </summary>
    /// <remarks>
    /// "Start from a document you already have" used to send people to the chat summariser, which
    /// read the file and then stopped — a dead end dressed as a beginning. This reads it, works
    /// out what it teaches, and puts a section on their path.
    /// </remarks>
    [HttpPost("sections/from-document")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> AddSectionFromDocumentAsync(
        [FromForm] IFormFile? file,
        CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (!DocumentTextExtractor.IsSupported(file.FileName, file.ContentType))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
            {
                error = "Unsupported format. PDF, Word, Markdown, text, CSV, JSON or HTML.",
            });
        }

        string text;
        try
        {
            text = await DocumentTextExtractor.ExtractTextAsync(file, ct);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Could not read this document: {ex.Message}" });
        }

        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new { error = "That file contained no readable text." });

        var subject = await _roadmaps.ReadSubjectFromDocumentAsync(text, file.FileName, ct);
        if (subject is null)
        {
            // Better to ask than to guess a subject off a filename.
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                error = "Radar could not tell what this document teaches. Try the three questions instead.",
            });
        }

        // A document with sections becomes a path. One without — the model could name the
        // subject but not break it up — falls back to the single-section route, which writes its
        // own lessons for that subject.
        var roadmap = subject.Sections is { Count: > 0 }
            ? await _roadmaps.AddDocumentPathAsync(profile.Id, subject, ct)
            : await _roadmaps.AddSectionAsync(
                profile.Id, profile, subject.Subject!, subject.Level ?? "Beginner", subject.Outcome, ct);

        return Ok(new { roadmap, detected = subject });
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

        // The ones that will still be there after they are chosen. A path generated before
        // anybody was asked reads as absent, so offering it in a switcher offers a path that
        // disappears the moment somebody picks it.
        return Ok(await _roadmaps.GetSwitchableRoadmapsAsync(profile.Id));
    }

    /// <summary>
    /// Switches to one of the learner's other paths.
    /// </summary>
    /// <remarks>
    /// Changing path meant going to Settings, editing the interests a path is derived from, and
    /// generating a new one - so paths somebody had already built were unreachable from the
    /// screen that lists them.
    /// </remarks>
    [HttpPost("{roadmapId}/activate")]
    public async Task<IActionResult> ActivateAsync(string roadmapId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var activated = await _roadmaps.ActivateRoadmapAsync(profile.Id, roadmapId);

        return activated is null
            ? NotFound(new { error = "That path is not one of yours." })
            : Ok(activated);
    }

    /// <summary>Takes a section out of a path.</summary>
    /// <remarks>
    /// Sections could be added and never removed, so a path built by trying things accumulated
    /// every wrong turn and the only way back was to start again.
    /// </remarks>
    [HttpDelete("{roadmapId}/modules/{moduleId}")]
    public async Task<IActionResult> RemoveSectionAsync(string roadmapId, string moduleId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var updated = await _roadmaps.RemoveSectionAsync(profile.Id, roadmapId, moduleId);

        return updated is null
            ? NotFound(new { error = "That path is not one of yours." })
            : Ok(updated);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveAsync(CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // The caller's cancellation reaches the video searches this triggers. Without it, a
        // reader who navigated away left a request still calling two third-party services.
        var active = await _roadmaps.GetActiveRoadmapAsync(profile.Id, ct);
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

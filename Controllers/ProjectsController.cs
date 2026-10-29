using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Helpers;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectStudioService _projects;
    private readonly ProjectAssessmentService _assessments;
    private readonly IRoadmapService _roadmaps;
    private readonly IUserProfileService _profiles;

    public ProjectsController(
        IProjectStudioService projects,
        IUserProfileService profiles,
        ProjectAssessmentService assessments,
        IRoadmapService roadmaps)
    {
        _projects = projects;
        _profiles = profiles;
        _assessments = assessments;
        _roadmaps = roadmaps;
    }

    /// <summary>
    /// What this learner should build next, from their own path.
    /// </summary>
    /// <remarks>
    /// This served six templates to everybody - Product Case Study, Marketing Campaign, Research
    /// Proposal, Literature Review, Business Model Canvas, Portfolio Project - so a learner
    /// studying mathematics was offered a Business Model Canvas. A fixed list shown to everyone
    /// is a catalogue, not a suggestion.
    ///
    /// Empty until there is a path to suggest from, which is the honest answer rather than
    /// falling back to the catalogue.
    /// </remarks>
    [HttpGet("suggested")]
    public async Task<IActionResult> GetSuggestedAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _projects.GetSuggestedProjectsAsync(profile.Id));
    }

    [HttpGet]
    public async Task<IActionResult> GetUserProjectsAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _projects.GetUserProjectsAsync(profile.Id));
    }

    [HttpPost("suggested/{projectId}/start")]
    public async Task<IActionResult> StartSuggestedAsync(string projectId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var project = await _projects.StartSuggestedAsync(profile.Id, projectId);

        // Not found rather than a 500, and said in terms of this learner: an id that is not among
        // their suggestions is not a missing record, it is somebody else's project.
        return project is null
            ? NotFound(new { error = "That is not one of the projects suggested for your path." })
            : Ok(project);
    }

    public sealed class CreateProjectRequest
    {
        [Required, MinLength(2), MaxLength(160)] public string Title { get; set; } = string.Empty;
        [Required, MinLength(2), MaxLength(4000)] public string Description { get; set; } = string.Empty;
        [Required, MinLength(2), MaxLength(80)] public string Category { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> CreateProjectAsync([FromBody] CreateProjectRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var project = await _projects.CreateProjectAsync(profile.Id, request.Title, request.Description, request.Category);
        // Named route rather than CreatedAtAction: this controller is attribute-routed, where
        // action-name-only URL generation does not resolve and throws instead of falling back.
        return CreatedAtRoute(GetProjectRouteName, new { projectId = project.Id }, project);
    }

    /// <summary>
    /// Builds and persists an AI execution plan for a project. Idempotent — calling it again
    /// returns the stored plan rather than regenerating (and re-billing) one.
    /// </summary>
    [HttpPost("{projectId}/plan")]
    public async Task<IActionResult> GeneratePlanAsync(string projectId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var project = await _projects.GeneratePlanAsync(profile.Id, projectId, profile);
        return project is null ? NotFound(new { error = "Project not found" }) : Ok(project);
    }

    [HttpGet("{projectId}/plan")]
    public async Task<IActionResult> GetProjectPlanAsync(string projectId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var project = await _projects.GetProjectAsync(profile.Id, projectId);
        if (project is null) return NotFound(new { error = "Project not found" });
        return project.Plan is null
            ? NotFound(new { error = "No plan has been generated for this project" })
            : Ok(project.Plan);
    }

    private const string GetProjectRouteName = "Projects_GetProject";

    [HttpGet("{projectId}", Name = GetProjectRouteName)]
    public async Task<IActionResult> GetProjectAsync(string projectId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var project = await _projects.GetProjectAsync(profile.Id, projectId);
        return project is null ? NotFound(new { error = "Project not found" }) : Ok(project);
    }

    /// <summary>
    /// Hands in an attempt at a project.
    /// </summary>
    /// <remarks>
    /// Five kinds, because what people build is not all one shape. What Radar can read of each
    /// differs, and the difference is recorded rather than smoothed over:
    ///
    /// A document or source file is read as text. A link is stored as a link. An image or a video
    /// cannot be read at all — the engine takes text and nothing else — so what gets assessed for
    /// those is the learner's own written account of what they made, and the assessment has to
    /// say that is what it judged. Scoring an artefact Radar never saw, without saying so, would
    /// be the same fabrication as a summary written from a headline.
    ///
    /// Notes are asked for on every kind, not only the ones that need them. They are what a
    /// reviewer reads first, and for an image or a video they are the whole of the evidence.
    /// </remarks>
    [HttpPost("{projectId}/submit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SubmitAsync(
        string projectId,
        [FromForm] SubmissionKind kind,
        [FromForm] IFormFile? file = null,
        [FromForm] string? url = null,
        [FromForm] string? notes = null,
        CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // The stage sets the bar. A first project is judged on finishing; work meant for an
        // employer is judged on the decisions behind it.
        var roadmap = await _roadmaps.GetActiveRoadmapAsync(profile.Id);
        var roadmapStage = roadmap?.CurrentStage ?? "Beginner";

        if (file is null && string.IsNullOrWhiteSpace(url))
            return BadRequest(new { error = "Attach a file or give a link to what you built." });

        var submission = new ProjectSubmission
        {
            Kind = kind,
            Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            FileName = file?.FileName,
            Notes = notes?.Trim() ?? string.Empty,
        };

        // Read what can be read. An image or a video leaves this empty, which is the honest
        // record of what Radar has rather than a gap for something to guess into later.
        if (file is not null && file.Length > 0 && kind is SubmissionKind.Document or SubmissionKind.Code)
        {
            if (!DocumentTextExtractor.IsSupported(file.FileName, file.ContentType))
            {
                return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
                {
                    error = "Radar cannot read that format. PDF, Word, Markdown, text, CSV, JSON or HTML.",
                });
            }

            try
            {
                submission.ExtractedText = await DocumentTextExtractor.ExtractTextAsync(file, ct);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Could not read that file: {ex.Message}" });
            }
        }

        // Fetched before assessing, because the assessor needs to know what the project was
        // meant to produce. Judging work against a blank brief marks somebody down for not doing
        // a thing nobody stated.
        var existing = await _projects.GetProjectAsync(profile.Id, projectId);
        if (existing is null) return NotFound(new { error = "That project is not one of yours." });

        // Assessed on arrival rather than on request. A project that sits unassessed is one a
        // learner has to remember to chase, and the point of a bar is that handing work in is
        // the moment it gets judged.
        submission.Assessment = await _assessments.AssessAsync(
            existing, submission, roadmapStage, ct);

        var project = await _projects.SubmitAsync(profile.Id, projectId, submission);

        return project is null
            ? NotFound(new { error = "That project is not one of yours." })
            : Ok(project);
    }

    [HttpPost("{projectId}/complete")]
    public async Task<IActionResult> CompleteProjectAsync(string projectId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // Refused when the work has not earned it, with the reason. A project is accepted on
        // what was handed in and how it scored, not on being marked done - which is what this
        // used to mean.
        var refusal = await _projects.CompleteProjectAsync(profile.Id, projectId);

        return refusal is null
            ? NoContent()
            : BadRequest(new { error = refusal });
    }

    public sealed class SetVisibilityRequest
    {
        [Required] public bool IsPublic { get; set; }
    }

    [HttpPost("{projectId}/visibility")]
    public async Task<IActionResult> SetVisibilityAsync(string projectId, [FromBody] SetVisibilityRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _projects.SetVisibilityAsync(profile.Id, projectId, request.IsPublic);
        return NoContent();
    }

    /// <summary>Public showcase view — no auth, only returns projects their owner made public.</summary>
    [AllowAnonymous]
    [HttpGet("{projectId}/public")]
    public async Task<IActionResult> GetPublicProjectAsync(string projectId)
    {
        var project = await _projects.GetPublicProjectAsync(projectId);
        return project is null ? NotFound(new { error = "Project not found" }) : Ok(project);
    }
}

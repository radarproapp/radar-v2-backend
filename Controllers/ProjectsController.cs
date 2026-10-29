using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectStudioService _projects;
    private readonly IUserProfileService _profiles;

    public ProjectsController(IProjectStudioService projects, IUserProfileService profiles)
    {
        _projects = projects;
        _profiles = profiles;
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

    [HttpPost("{projectId}/complete")]
    public async Task<IActionResult> CompleteProjectAsync(string projectId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _projects.CompleteProjectAsync(profile.Id, projectId);
        return NoContent();
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

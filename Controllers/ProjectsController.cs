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

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplatesAsync() => Ok(await _projects.GetTemplatesAsync());

    [HttpGet]
    public async Task<IActionResult> GetUserProjectsAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _projects.GetUserProjectsAsync(profile.Id));
    }

    [HttpPost("{templateId}/start")]
    public async Task<IActionResult> StartProjectAsync(string templateId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var project = await _projects.StartProjectAsync(profile.Id, templateId);
        return Ok(project);
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

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
}

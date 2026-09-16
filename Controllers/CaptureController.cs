using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/capture")]
public class CaptureController : ControllerBase
{
    private readonly ICaptureService _capture;
    private readonly IUserProfileService _profiles;

    public CaptureController(ICaptureService capture, IUserProfileService profiles)
    {
        _capture = capture;
        _profiles = profiles;
    }

    public sealed class CaptureRequest
    {
        [Required] public CaptureMode Mode { get; set; }
        [Required, MinLength(1)] public string Input { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> CaptureAsync([FromBody] CaptureRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var item = await _capture.CaptureAsync(profile.Id, request.Mode, request.Input.Trim());
        return Ok(item);
    }

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _capture.GetRecentCapturesAsync(profile.Id));
    }
}

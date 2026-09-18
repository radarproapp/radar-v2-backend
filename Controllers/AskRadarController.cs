using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Ask Radar — general-purpose chat grounded in the user's profile. Chat is a plain
/// request/response call for now: IAskRadarService also exposes StreamMessageAsync (SSE-style),
/// but wiring real-time streaming is an explicitly separate, later phase (REACT_MIGRATION.md §5
/// Phase 3 step 5) — not part of this port.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ask")]
public class AskRadarController : ControllerBase
{
    private readonly IAskRadarService _ask;
    private readonly IUserProfileService _profiles;

    public AskRadarController(IAskRadarService ask, IUserProfileService profiles)
    {
        _ask = ask;
        _profiles = profiles;
    }

    public sealed class ChatRequest
    {
        [Required, MinLength(1)] public string Message { get; set; } = string.Empty;
        public List<ChatMessage> History { get; set; } = [];
    }

    [HttpPost("chat")]
    public async Task<IActionResult> ChatAsync([FromBody] ChatRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var response = await _ask.SendMessageAsync(profile.Id, request.Message, request.History);
        return Ok(response);
    }
}

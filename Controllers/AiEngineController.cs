using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Diagnostic and telemetry endpoints for Radar's Central AI Engine.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ai")]
public class AiEngineController : ControllerBase
{
    private readonly IAiEngine _ai;

    public AiEngineController(IAiEngine ai)
    {
        _ai = ai;
    }

    /// <summary>
    /// Returns the live operational status, token/spend metrics, and activity processing distribution
    /// across all Radar subsystems (Feed, Learning Path, Learn Hub, Clips, Opportunities, Research, Mentor).
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<AiEngineStats>> GetStatusAsync(CancellationToken ct = default)
    {
        var stats = await _ai.GetEngineStatsAsync(ct);
        return Ok(stats);
    }
}

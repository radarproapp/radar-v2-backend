using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Manual review surface for content reports (product-strategy §"source quality needs continuous
/// review"). No admin-role system exists yet, so authenticated-only for now, same as
/// EventsController's /summary -- nothing links to this from the primary nav.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IContentReportService _reports;

    public ReportsController(IContentReportService reports) => _reports = reports;

    [HttpGet]
    public async Task<IActionResult> GetOpenReportsAsync() => Ok(await _reports.GetOpenReportsAsync());

    [HttpGet("sources")]
    public async Task<IActionResult> GetSourceSummaryAsync([FromQuery] int days = 30) =>
        Ok(await _reports.GetSourceSummaryAsync(Math.Clamp(days, 1, 365)));

    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> ResolveAsync(string id)
    {
        await _reports.ResolveReportAsync(id);
        return NoContent();
    }
}

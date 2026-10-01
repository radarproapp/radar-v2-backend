using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;
using RadarV2.Models;

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
    private readonly IIntelligenceFeedService _feed;
    private readonly IUserProfileService _profiles;

    public ReportsController(IContentReportService reports, IIntelligenceFeedService feed, IUserProfileService profiles)
    {
        _reports = reports;
        _feed = feed;
        _profiles = profiles;
    }

    public sealed class CreateReportRequest
    {
        public string ContentItemId { get; set; } = string.Empty;
        public ReportReason Reason { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContentItemId))
            return BadRequest(new { error = "ContentItemId is required." });

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();
        var item = await _feed.GetByIdAsync(request.ContentItemId);
        if (item is null) return NotFound(new { error = "Content item not found." });

        await _reports.CreateReportAsync(profile.Id, item, request.Reason, request.Note?.Trim());
        return Accepted(new { submitted = true });
    }

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

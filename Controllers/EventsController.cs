using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IAnalyticsService _analytics;
    private readonly IUserProfileService _profiles;
    private readonly IPersonalizedWhyService _why;

    public EventsController(IAnalyticsService analytics, IUserProfileService profiles, IPersonalizedWhyService why)
    {
        _analytics = analytics;
        _profiles = profiles;
        _why = why;
    }

    public sealed class LogEventRequest
    {
        [Required] public AnalyticsEventType Type { get; set; }
        public string? ContentItemId { get; set; }
        public string? OpportunityId { get; set; }
        public string? ClipId { get; set; }
        public int? Position { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> LogAsync([FromBody] LogEventRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _analytics.LogEventAsync(new AnalyticsEvent
        {
            UserId = profile.Id,
            Type = request.Type,
            ContentItemId = request.ContentItemId,
            OpportunityId = request.OpportunityId,
            ClipId = request.ClipId,
            Position = request.Position,
            Metadata = request.Metadata ?? [],
        });

        return NoContent();
    }

    // No admin-role system exists yet, so this is authenticated-only for now rather than
    // admin-gated — fine for a first pass since nothing links to it from the UI yet.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummaryAsync([FromQuery] int days = 7)
    {
        var clampedDays = Math.Clamp(days, 1, 90);
        var counts = await _analytics.GetEventCountsAsync(clampedDays);
        var (helpful, notHelpful) = await _why.GetHelpfulRateAsync(clampedDays);
        var total = helpful + notHelpful;

        return Ok(new
        {
            eventCounts = counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            whyHelpfulRatings = new { helpful, notHelpful, helpfulRatePercent = total == 0 ? (double?)null : Math.Round(100.0 * helpful / total, 1) },
        });
    }
}

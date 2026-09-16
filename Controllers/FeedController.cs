using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/feed")]
public class FeedController : ControllerBase
{
    private readonly IIntelligenceFeedService _feed;
    private readonly IUserProfileService _profiles;
    private readonly IPersonalizedWhyService _why;
    private readonly IAnalyticsService _analytics;

    public FeedController(
        IIntelligenceFeedService feed,
        IUserProfileService profiles,
        IPersonalizedWhyService why,
        IAnalyticsService analytics)
    {
        _feed = feed;
        _profiles = profiles;
        _why = why;
        _analytics = analytics;
    }

    [HttpGet]
    public async Task<IActionResult> GetFeedAsync(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        var items = await _feed.GetFeedAsync(profile, ParseType(type), Math.Max(1, page), Math.Clamp(pageSize, 1, 50));
        return Ok(items);
    }

    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedAsync()
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        return Ok(await _feed.GetSavedItemsAsync(profile.Id));
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] string q = "",
        [FromQuery] string? type = null)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "A search query 'q' is required." });

        var items = await _feed.SearchAsync(q.Trim(), ParseType(type));
        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(string id)
    {
        var item = await _feed.GetByIdAsync(id);
        return item is null
            ? NotFound(new { error = "Item not found" })
            : Ok(item);
    }

    [HttpPost("{id}/save")]
    public async Task<IActionResult> SaveAsync(string id)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        await _feed.SaveItemAsync(profile.Id, id);
        return NoContent();
    }

    [HttpDelete("{id}/save")]
    public async Task<IActionResult> UnsaveAsync(string id)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        await _feed.UnsaveItemAsync(profile.Id, id);
        return NoContent();
    }

    public sealed class WhyRatingRequest
    {
        [Required] public bool Helpful { get; set; }
    }

    /// <summary>
    /// Personalized "why this matters" for this user, generated/cached on demand. Falls back to
    /// the item's generic WhyItMatters (isPersonalized=false) when generation isn't possible
    /// right now — see IPersonalizedWhyService for why that's not a hard failure.
    /// </summary>
    [HttpGet("{id}/why")]
    public async Task<IActionResult> GetWhyAsync(string id)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        var item = await _feed.GetByIdAsync(id);
        if (item is null) return NotFound(new { error = "Item not found" });

        var personalized = await _why.GetOrGenerateAsync(profile, item);
        return Ok(new
        {
            text = personalized?.WhyText ?? item.WhyItMatters,
            isPersonalized = personalized?.IsGenerated ?? false,
            isHelpful = personalized?.IsHelpful,
        });
    }

    [HttpPost("{id}/why-rating")]
    public async Task<IActionResult> RateWhyAsync(string id, [FromBody] WhyRatingRequest request)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        var item = await _feed.GetByIdAsync(id);
        if (item is null) return NotFound(new { error = "Item not found" });

        var personalized = await _why.GetOrGenerateAsync(profile, item);
        await _why.RateAsync(profile.Id, id, personalized?.WhyText ?? item.WhyItMatters, request.Helpful);
        await _analytics.LogEventAsync(new AnalyticsEvent
        {
            UserId = profile.Id,
            Type = request.Helpful ? AnalyticsEventType.WhyRatedHelpful : AnalyticsEventType.WhyRatedNotHelpful,
            ContentItemId = id,
        });

        return NoContent();
    }

    private async Task<UserProfile?> RequireProfileAsync() => await _profiles.GetCurrentUserAsync();

    private static ContentType? ParseType(string? type) =>
        Enum.TryParse<ContentType>(type, ignoreCase: true, out var parsed) ? parsed : null;
}

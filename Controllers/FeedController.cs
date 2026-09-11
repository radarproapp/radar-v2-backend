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

    public FeedController(IIntelligenceFeedService feed, IUserProfileService profiles)
    {
        _feed = feed;
        _profiles = profiles;
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

    private async Task<UserProfile?> RequireProfileAsync() => await _profiles.GetCurrentUserAsync();

    private static ContentType? ParseType(string? type) =>
        Enum.TryParse<ContentType>(type, ignoreCase: true, out var parsed) ? parsed : null;
}

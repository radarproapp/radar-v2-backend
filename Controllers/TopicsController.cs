using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/topics")]
public class TopicsController : ControllerBase
{
    private readonly ITopicService _topics;
    private readonly IUserProfileService _profiles;

    public TopicsController(ITopicService topics, IUserProfileService profiles)
    {
        _topics = topics;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _topics.GetTopicsAsync(profile.Id));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAsync(string id)
    {
        var topic = await _topics.GetTopicAsync(id);
        return topic is null
            ? NotFound(new { error = "Topic not found" })
            : Ok(topic);
    }

    [HttpPost("{id}/follow")]
    public async Task<IActionResult> ToggleFollowAsync(string id)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _topics.ToggleFollowAsync(profile.Id, id);
        return NoContent();
    }

    [HttpPost("{id}/alerts/{alertIndex:int}")]
    public async Task<IActionResult> ToggleAlertAsync(string id, int alertIndex)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _topics.ToggleAlertAsync(profile.Id, id, alertIndex);
        return NoContent();
    }
}

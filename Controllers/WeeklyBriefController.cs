using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/weekly-brief")]
public class WeeklyBriefController : ControllerBase
{
    private readonly IWeeklyBriefService _briefs;
    private readonly IIntelligenceFeedService _feed;
    private readonly IOpportunityService _opportunities;
    private readonly IUserProfileService _profiles;

    public WeeklyBriefController(
        IWeeklyBriefService briefs,
        IIntelligenceFeedService feed,
        IOpportunityService opportunities,
        IUserProfileService profiles)
    {
        _briefs = briefs;
        _feed = feed;
        _opportunities = opportunities;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // Auto-generate if no brief exists for this week, same as the Blazor page did.
        var brief = await _briefs.GetLatestBriefAsync(profile.Id)
            ?? await _briefs.GenerateBriefAsync(profile.Id, profile);

        if (brief.TopArticles.Count == 0)
            brief.TopArticles = await _feed.GetFeedAsync(profile, ContentType.Article, pageSize: 3);
        if (brief.TopResearchPapers.Count == 0)
            brief.TopResearchPapers = await _feed.GetFeedAsync(profile, ContentType.ResearchPaper, pageSize: 2);
        if (brief.TopVideos.Count == 0)
            brief.TopVideos = await _feed.GetFeedAsync(profile, ContentType.Video, pageSize: 2);
        if (brief.TopPodcasts.Count == 0)
            brief.TopPodcasts = await _feed.GetFeedAsync(profile, ContentType.Podcast, pageSize: 2);
        if (brief.TopOpportunities.Count == 0)
            brief.TopOpportunities = await _opportunities.GetOpportunitiesAsync(profile, pageSize: 3);

        return Ok(brief);
    }
}

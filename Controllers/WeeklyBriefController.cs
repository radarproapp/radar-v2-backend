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
    private readonly IUserProfileService _profiles;

    public WeeklyBriefController(
        IWeeklyBriefService briefs,
        IIntelligenceFeedService feed,
        IUserProfileService profiles)
    {
        _briefs = briefs;
        _feed = feed;
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

        // News/articles/blog posts/videos/podcasts only — no opportunities or research papers
        // (those belong to Opportunities and Learn Hub/Library respectively, not the brief).
        if (brief.TopArticles.Count == 0)
        {
            var articles = await _feed.GetFeedAsync(profile, ContentType.Article, pageSize: 3);
            var blogPosts = await _feed.GetFeedAsync(profile, ContentType.Essay, pageSize: 2);
            brief.TopArticles = [.. articles, .. blogPosts];
        }
        if (brief.TopVideos.Count == 0)
            brief.TopVideos = await _feed.GetFeedAsync(profile, ContentType.Video, pageSize: 2);
        if (brief.TopPodcasts.Count == 0)
            brief.TopPodcasts = await _feed.GetFeedAsync(profile, ContentType.Podcast, pageSize: 2);

        return Ok(brief);
    }
}

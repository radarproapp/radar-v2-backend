using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Helpers;
using RadarV2.Models;
using RadarV2.Services.Interfaces;
using RadarV2.Services;

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
    private readonly IContentReportService _reports;
    private readonly PlaybackService _playback;

    public FeedController(
        IIntelligenceFeedService feed,
        IUserProfileService profiles,
        IPersonalizedWhyService why,
        IAnalyticsService analytics,
        IContentReportService reports,
        PlaybackService playback)
    {
        _feed = feed;
        _profiles = profiles;
        _why = why;
        _analytics = analytics;
        _reports = reports;
        _playback = playback;
    }

    /// <summary>
    /// Fields a caller may request via <c>?fields=</c>. An explicit whitelist rather than reflection:
    /// it documents the public shape of a feed item, and keeps a client from being able to enumerate
    /// fields we never meant to expose.
    /// </summary>
    private static readonly Dictionary<string, Func<ContentItem, object?>> FeedFieldProjectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = i => i.Id,
        ["type"] = i => i.Type,
        ["title"] = i => i.Title,
        ["signal"] = i => i.Signal,
        ["topic"] = i => i.Topic,
        ["secondaryTopics"] = i => i.SecondaryTopics,
        ["source"] = i => i.Source,
        ["sourceLogoUrl"] = i => i.SourceLogoUrl,
        ["url"] = i => i.Url,
        ["thumbnailUrl"] = i => i.ThumbnailUrl,
        ["publishedAt"] = i => i.PublishedAt,
        ["whyItMatters"] = i => i.WhyItMatters,
        ["whyYoureSeeingThis"] = i => i.WhyYoureSeeingThis,
        ["aiSummary"] = i => i.AiSummary,
        ["whatHappened"] = i => i.WhatHappened,
        ["personalizedWhy"] = i => i.PersonalizedWhy,
        ["nextMove"] = i => i.NextMove,
        ["matchedSignals"] = i => i.MatchedSignals,
        ["relevanceScore"] = i => i.RelevanceScore,
        ["layer"] = i => i.Layer,
        ["credibilityTier"] = i => i.CredibilityTier,
        ["isSaved"] = i => i.IsSaved,
        ["tags"] = i => i.Tags,
        ["keyInsights"] = i => i.KeyInsights,
        ["opportunities"] = i => i.Opportunities,
        ["estimatedReadTime"] = i => i.EstimatedReadTime,
        ["estimatedWatchTime"] = i => i.EstimatedWatchTime,
        ["audioUrl"] = i => i.AudioUrl,
        ["durationSeconds"] = i => i.DurationSeconds,
        ["authors"] = i => i.Authors,
        ["journal"] = i => i.Journal,
        ["hasSummary"] = i => i.IsEnriched,
    };

    /// <summary>
    /// The intelligence feed, keyset-paginated. Pass the returned <c>nextCursor</c> back as
    /// <c>?cursor=</c> to fetch the following page; <c>nextCursor</c> is null on the last page.
    ///
    /// A cursor rather than a page number because the feed grows while it is being read: with an
    /// offset, anything ingested between two requests shifts the window and the reader silently sees
    /// duplicate or skipped items. <c>page</c>/<c>pageSize</c> are intentionally gone — they were
    /// exactly the shifting window this replaces.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFeedAsync(
        [FromQuery] string? type = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        [FromQuery] string? fields = null)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        if (!FieldSet.TryParse(fields, FeedFieldProjectors.Keys.ToList(), out var fieldSet, out var fieldError))
            return BadRequest(new { error = fieldError });

        var page = await _feed.GetFeedPageAsync(profile, ParseType(type), cursor, limit);
        return Ok(fieldSet.ProjectPage(page, FeedFieldProjectors));
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
    public async Task<IActionResult> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var item = await _feed.GetByIdAsync(id);
        if (item is null) return NotFound(new { error = "Item not found" });

        // Returned with the item rather than from a second request, so the player can open at the
        // resume point instead of starting at zero and jumping once a follow-up lands.
        var profile = await _profiles.GetCurrentUserAsync();
        var progress = profile is null ? null : await _playback.GetAsync(profile.Id, id, ct);

        return Ok(new
        {
            item,
            playback = progress is null ? null : new
            {
                positionSeconds = progress.PositionSeconds,
                durationSeconds = progress.DurationSeconds,
                secondsPlayed = progress.SecondsPlayed,
                completedAt = progress.CompletedAt,
            },
        });
    }

    public sealed class PlaybackHeartbeatRequest
    {
        /// <summary>Where the player is now, in seconds.</summary>
        public double PositionSeconds { get; set; }

        /// <summary>Total runtime, when the element knows it.</summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// Seconds that elapsed while actually playing since the last heartbeat.
        /// </summary>
        /// <remarks>
        /// A delta, not a total. The server clamps it and keeps the running sum, so a paused tab
        /// left open overnight and a scrub to the end both add nothing — which is what lets
        /// "4h 12m listened" mean listened.
        /// </remarks>
        public double PlayedDeltaSeconds { get; set; }
    }

    /// <summary>Records playback position and genuinely played time.</summary>
    [HttpPut("{id}/progress")]
    public async Task<IActionResult> RecordProgressAsync(
        string id, [FromBody] PlaybackHeartbeatRequest request, CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return Unauthorized();

        var item = await _feed.GetByIdAsync(id);
        if (item is null) return NotFound(new { error = "Item not found" });

        var wasComplete = (await _playback.GetAsync(profile.Id, id, ct))?.CompletedAt is not null;

        var progress = await _playback.RecordAsync(
            profile.Id, id, request.PositionSeconds, request.DurationSeconds, request.PlayedDeltaSeconds, ct);

        // Logged once, on the heartbeat that crossed the line. The graph counts finished items
        // from these events, and an event per heartbeat would count one episode forever.
        if (!wasComplete && progress.CompletedAt is not null)
        {
            await _analytics.LogEventAsync(new AnalyticsEvent
            {
                UserId = profile.Id,
                Type = AnalyticsEventType.Complete,
                ContentItemId = id,
            });
        }

        return Ok(new
        {
            positionSeconds = progress.PositionSeconds,
            secondsPlayed = progress.SecondsPlayed,
            completedAt = progress.CompletedAt,
        });
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

    /// <summary>
    /// Marks an item as not relevant for this user — excluded from GetFeedAsync from now on.
    /// Analytics logging is a separate client call to POST /api/events (same pattern as save/unsave).
    /// </summary>
    [HttpPost("{id}/dismiss")]
    public async Task<IActionResult> DismissAsync(string id)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        await _feed.DismissItemAsync(profile.Id, id);
        return NoContent();
    }

    /// <summary>
    /// Brings every dismissed item back.
    ///
    /// The empty feed can now say "you have dismissed everything that matches", which is only
    /// better than a blank page if the user can do something about it. Until this existed, a
    /// dismissal was permanent and there was no way to undo one.
    /// </summary>
    [HttpDelete("dismissed")]
    public async Task<IActionResult> RestoreDismissedAsync()
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        var restored = await _feed.RestoreDismissedAsync(profile.Id);
        return Ok(new { restored });
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
    /// <summary>The first of these that is actually populated.</summary>
    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

    [HttpGet("{id}/why")]
    public async Task<IActionResult> GetWhyAsync(string id)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        var item = await _feed.GetByIdAsync(id);
        if (item is null) return NotFound(new { error = "Item not found" });

        var deterministic = InterestPersonalization.Explain(profile, item);
        var personalized = await _why.GetOrGenerateAsync(profile, item);

        // Three sources, in order of how much they know: the generated explanation, the
        // deterministic one, and — when neither has anything true to say because nothing in the
        // profile matched — the same honest line the card carries. An empty answer to "why am I
        // seeing this" is the one response the page cannot render.
        var whyText = FirstNonEmpty(
            personalized?.WhyText,
            deterministic.Why,
            item.WhyYoureSeeingThis,
            WhyShown.Build(item, deterministic.MatchedSignals));
        var nextMove = !string.IsNullOrWhiteSpace(personalized?.NextMove) ? personalized.NextMove : deterministic.NextMove;

        return Ok(new
        {
            text = whyText,
            whatToKnow = InterestPersonalization.WhatToKnow(item),
            nextMove = nextMove,
            relevanceScore = item.RelevanceScore,
            relevanceConfidence = item.RelevanceConfidence,
            matchedSignals = item.MatchedSignals.Count > 0 ? item.MatchedSignals : deterministic.MatchedSignals,
            isPersonalized = personalized?.IsGenerated ?? false,
            // Surfaced so the feed can later drop weakly-connected items instead of explaining them.
            relevance = personalized?.Relevance,
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

        var deterministic = InterestPersonalization.Explain(profile, item);
        var personalized = await _why.GetOrGenerateAsync(profile, item);
        var whyText = FirstNonEmpty(
            personalized?.WhyText,
            deterministic.Why,
            item.WhyYoureSeeingThis,
            WhyShown.Build(item, deterministic.MatchedSignals));

        await _why.RateAsync(profile.Id, id, whyText, request.Helpful);
        await _analytics.LogEventAsync(new AnalyticsEvent
        {
            UserId = profile.Id,
            Type = request.Helpful ? AnalyticsEventType.WhyRatedHelpful : AnalyticsEventType.WhyRatedNotHelpful,
            ContentItemId = id,
        });

        return NoContent();
    }

    public sealed class ReportRequest
    {
        [Required] public ReportReason Reason { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("{id}/report")]
    public async Task<IActionResult> ReportAsync(string id, [FromBody] ReportRequest request)
    {
        var profile = await RequireProfileAsync();
        if (profile is null) return Unauthorized();

        var item = await _feed.GetByIdAsync(id);
        if (item is null) return NotFound(new { error = "Item not found" });

        await _reports.CreateReportAsync(profile.Id, item, request.Reason, request.Note);
        await _analytics.LogEventAsync(new AnalyticsEvent
        {
            UserId = profile.Id,
            Type = AnalyticsEventType.Reported,
            ContentItemId = id,
            Metadata = new Dictionary<string, string> { ["reason"] = request.Reason.ToString() },
        });

        return NoContent();
    }

    private async Task<UserProfile?> RequireProfileAsync() => await _profiles.GetCurrentUserAsync();

    private static ContentType? ParseType(string? type) =>
        Enum.TryParse<ContentType>(type, ignoreCase: true, out var parsed) ? parsed : null;
}

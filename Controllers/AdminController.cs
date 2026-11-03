using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Services;

namespace RadarV2.Controllers;

[ApiController]
[Authorize(Roles = "SuperAdmin,PlatformAdmin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly RadarDatabase _db;
    private readonly AdminInsightsService _insights;
    private readonly ILogger<AdminController> _log;

    public AdminController(RadarDatabase db, AdminInsightsService insights, ILogger<AdminController> log)
    {
        _db = db;
        _insights = insights;
        _log = log;
    }

    /// <summary>
    /// The most recent unhandled exceptions, newest first.
    /// </summary>
    /// <remarks>
    /// Production responses carry no error detail on purpose, so without this the only way to see
    /// why something is failing there is the host's log dashboard. Admin-gated, because a stack
    /// trace is not for everyone.
    /// </remarks>
    [HttpGet("errors")]
    public async Task<IActionResult> GetRecentErrorsAsync([FromQuery] int take = 20, CancellationToken ct = default)
    {
        var errors = await _db.ErrorLog
            .Find(FilterDefinition<Data.Documents.ErrorLogDoc>.Empty)
            .SortByDescending(e => e.OccurredAt)
            .Limit(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return Ok(errors);
    }

    /// <summary>
    /// What every ingestion source last did, worst first.
    /// </summary>
    /// <remarks>
    /// The question this exists to answer is "why is the feed missing X?". Before it, that took
    /// adding a source, waiting, probing the feed and inferring — four rounds, in the case that
    /// prompted it, each one finding a different silent failure. Now it is one request.
    ///
    /// Sorted by how long a source has been silent, because the interesting ones are the ones
    /// that have stopped.
    /// </remarks>
    [HttpGet("source-health")]
    public async Task<IActionResult> GetSourceHealthAsync(CancellationToken ct = default)
    {
        var health = await _db.SourceHealth
            .Find(FilterDefinition<Data.Documents.SourceHealthDoc>.Empty)
            .ToListAsync(ct);

        var byId = health.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        var rows = Services.Ingestion.SourceRegistry.All
            .Where(source => source.IsEnabled)
            .Select(source =>
            {
                byId.TryGetValue(source.Id, out var h);
                return new
                {
                    id = source.Id,
                    name = source.Name,
                    layer = source.Layer.ToString(),
                    // Never fetched at all is a different failure from fetched and empty, and the
                    // two were indistinguishable for as long as there was nothing to look at.
                    state = h is null ? "never attempted"
                        : h.LastSuccessAt is null ? "never produced an item"
                        : h.ConsecutiveEmpty >= 3 ? "silent"
                        : "producing",
                    lastAttemptAt = h?.LastAttemptAt,
                    lastSuccessAt = h?.LastSuccessAt,
                    hoursSinceSuccess = h?.LastSuccessAt is null
                        ? (double?)null
                        : Math.Round((now - h.LastSuccessAt.Value).TotalHours, 1),
                    lastHttpStatus = h?.LastHttpStatus,
                    lastItemsFetched = h?.LastItemsFetched,
                    consecutiveEmpty = h?.ConsecutiveEmpty,
                    lastError = h?.LastError,
                };
            })
            .OrderByDescending(r => r.state == "never attempted")
            .ThenByDescending(r => r.state == "never produced an item")
            .ThenByDescending(r => r.consecutiveEmpty ?? 0)
            .ToList();

        return Ok(new
        {
            total = rows.Count,
            producing = rows.Count(r => r.state == "producing"),
            silent = rows.Count(r => r.state == "silent"),
            neverProduced = rows.Count(r => r.state == "never produced an item"),
            neverAttempted = rows.Count(r => r.state == "never attempted"),
            sources = rows,
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummaryAsync(CancellationToken ct)
    {
        var openReports = await _db.ContentReports.CountDocumentsAsync(
            Builders<Models.ContentReport>.Filter.Eq(r => r.IsResolved, false), cancellationToken: ct);

        return Ok(new
        {
            users = await _db.Profiles.CountDocumentsAsync(FilterDefinition<Models.UserProfile>.Empty, cancellationToken: ct),
            publishedContent = await _db.ContentItems.CountDocumentsAsync(FilterDefinition<Models.ContentItem>.Empty, cancellationToken: ct),
            openReports,
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User"
        });
    }

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueueAsync([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var items = await _db.ContentItems.Find(_ => true)
            .SortByDescending(item => item.PublishedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Ok(items.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            signal = item.Signal,
            source = item.Source,
            publishedAt = item.PublishedAt,
            credibilityTier = item.CredibilityTier,
            isEnriched = item.IsEnriched,
            type = item.Type.ToString(),
        }));
    }

    [HttpGet("sources")]
    public async Task<IActionResult> GetSourcesAsync(CancellationToken ct)
    {
        var sources = await _db.Sources.Find(_ => true).SortBy(s => s.Name).ToListAsync(ct);
        return Ok(sources.Select(source => new
        {
            id = source.Id,
            name = source.Name,
            domain = source.Domain,
            type = source.Type,
            itemsInRadar = source.ItemsInRadar,
            itemsRead = source.ItemsRead,
            publishFrequency = source.PublishFrequency,
            active = true,
        }));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsersAsync([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var users = await _db.Profiles.Find(_ => true)
            .SortByDescending(profile => profile.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Ok(users.Select(profile => new
        {
            id = profile.Id,
            name = profile.Name,
            email = profile.Email,
            persona = profile.Persona.ToString(),
            goal = profile.PrimaryGoal,
            region = string.Join(", ", new[] { profile.City, profile.Region }.Where(value => !string.IsNullOrWhiteSpace(value))),
            onboardingComplete = profile.OnboardingComplete,
            roadmapProgressPercent = profile.Stats.RoadmapProgressPercent,
            createdAt = profile.CreatedAt,
        }));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReportsAsync(CancellationToken ct)
    {
        var reports = await _db.ContentReports.Find(report => !report.IsResolved)
            .SortByDescending(report => report.CreatedAt)
            .ToListAsync(ct);
        return Ok(reports.Select(report => new
        {
            id = report.Id,
            signal = report.ItemSignal,
            source = report.Source,
            reason = report.Reason.ToString(),
            note = report.Note,
            createdAt = report.CreatedAt,
        }));
    }

    [HttpPost("reports/{id}/resolve")]
    public async Task<IActionResult> ResolveReportAsync(string id, CancellationToken ct)
    {
        await _db.ContentReports.UpdateOneAsync(
            report => report.Id == id,
            Builders<Models.ContentReport>.Update.Set(report => report.IsResolved, true),
            cancellationToken: ct);
        return NoContent();
    }

    // ── what is actually happening ───────────────────────────────────────────

    /// <summary>
    /// Events and distinct people per day.
    /// </summary>
    /// <remarks>
    /// Eighteen kinds of event were already being recorded and nothing in the admin area read
    /// any of them, so "what are people engaging with" was a question the database could answer
    /// and the product could not.
    /// </remarks>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivityAsync([FromQuery] int days = 30, CancellationToken ct = default)
    {
        return Ok(new
        {
            days,
            byDay = await _insights.ActivityByDayAsync(days, ct),
            mix = await _insights.EventMixAsync(days, ct),
            funnel = await _insights.FunnelAsync(days, ct),

            // Said out loud, because the numbers above are only as good as what the client
            // sends - and today it fires seven of the eighteen kinds. A dashboard that does not
            // say so invites its own figures to be read as the whole picture.
            recorded = Enum.GetValues<Models.AnalyticsEventType>().Select(type => type.ToString()),
        });
    }

    /// <summary>What people opened and saved, and which sources earned it.</summary>
    [HttpGet("engagement")]
    public async Task<IActionResult> GetEngagementAsync([FromQuery] int days = 30, CancellationToken ct = default)
    {
        return Ok(new
        {
            days,
            items = await _insights.MostEngagedAsync(days, 20, ct),
            sources = await _insights.BySourceAsync(days, 25, ct),
        });
    }

    /// <summary>
    /// One person: what they told Radar, and what they have done since.
    /// </summary>
    /// <remarks>
    /// SuperAdmin only, and logged. The rest of this controller is aggregate; this is one named
    /// person's reading history, which is a different thing to hold and a different thing to
    /// justify. It exists because support and abuse questions cannot be answered from totals -
    /// and the audit line is so that looking at it is itself a recorded act, rather than a
    /// capability nobody can see being used.
    /// </remarks>
    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUserAsync(string id, CancellationToken ct = default)
    {
        var profile = await _db.Profiles.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var who = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _log.LogInformation("Admin {Admin} opened the detail for profile {Profile}", who, id);

        var events = await _db.AnalyticsEvents
            .Find(e => e.UserId == id)
            .SortByDescending(e => e.CreatedAt)
            .Limit(100)
            .ToListAsync(ct);

        var contentIds = events.Where(e => e.ContentItemId is not null).Select(e => e.ContentItemId!).Distinct().ToList();

        var titles = contentIds.Count == 0
            ? []
            : await _db.ContentItems
                .Find(Builders<Models.ContentItem>.Filter.In(item => item.Id, contentIds))
                .Project(item => new { item.Id, item.Title, item.Source })
                .ToListAsync(ct);

        var titleOf = titles.ToDictionary(t => t.Id);

        return Ok(new
        {
            profile = new
            {
                profile.Id,
                profile.Name,
                profile.Email,
                persona = profile.Persona.ToString(),
                chosenCategory = profile.HasChosenCategory,
                profile.Interests,
                profile.Goals,
                problems = profile.Problems,
                profile.PrimaryGoal,
                profile.IsPersonalised,
                profile.OnboardingComplete,
                profile.CreatedAt,
            },

            // Last seen is the newest event there is. Null rather than a guess when they have
            // done nothing: an account that has never acted has no last-seen, and showing their
            // sign-up date in that column would read as activity.
            lastSeen = events.Count > 0 ? events[0].CreatedAt : (DateTime?)null,

            recent = events.Select(e => new
            {
                type = e.Type.ToString(),
                at = e.CreatedAt,
                contentItemId = e.ContentItemId,
                title = e.ContentItemId is not null && titleOf.TryGetValue(e.ContentItemId, out var item) ? item.Title : null,
                source = e.ContentItemId is not null && titleOf.TryGetValue(e.ContentItemId, out var where) ? where.Source : null,
            }),
        });
    }
}

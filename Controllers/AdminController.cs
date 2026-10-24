using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;

namespace RadarV2.Controllers;

[ApiController]
[Authorize(Roles = "SuperAdmin,PlatformAdmin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly RadarDatabase _db;

    public AdminController(RadarDatabase db) => _db = db;

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
}

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

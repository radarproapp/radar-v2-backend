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
}

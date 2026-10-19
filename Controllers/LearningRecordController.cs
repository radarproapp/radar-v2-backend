using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// What Radar knows about this user's learning, in one place.
///
/// Exposed deliberately: personalisation that the user cannot see or correct is indistinguishable
/// from personalisation that is not happening, which is exactly how the old hardcoded screens felt.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/learning-record")]
public class LearningRecordController : ControllerBase
{
    private readonly IUserLearningRecordService _records;
    private readonly IUserProfileService _profiles;

    public LearningRecordController(IUserLearningRecordService records, IUserProfileService profiles)
    {
        _records = records;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _records.BuildAsync(profile, ct));
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/comparisons")]
public class CompareController : ControllerBase
{
    private readonly ICompareService _compare;
    private readonly IUserProfileService _profiles;

    public CompareController(ICompareService compare, IUserProfileService profiles)
    {
        _compare = compare;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _compare.GetComparisonsAsync(profile.Id));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(string id)
    {
        var comparison = await _compare.GetComparisonAsync(id);
        return comparison is null
            ? NotFound(new { error = "Comparison not found" })
            : Ok(comparison);
    }

    public sealed class CreateComparisonRequest
    {
        [Required, MinLength(2)] public List<string> ItemIds { get; set; } = [];
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateComparisonRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var comparison = await _compare.CreateComparisonAsync(profile.Id, request.ItemIds);
        return Ok(comparison);
    }
}

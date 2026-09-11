using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly IUserProfileService _profiles;

    public MeController(IUserProfileService profiles) => _profiles = profiles;

    /// <summary>
    /// Editable profile fields. Email/id/stats are managed server-side only —
    /// editing email would desync the auth 'users' collection, so it is excluded.
    /// </summary>
    public sealed class UpdateProfileRequest
    {
        public string? Name { get; set; }
        public string? Persona { get; set; }
        public string? PrimaryGoal { get; set; }
        public string? Region { get; set; }
        public string? City { get; set; }
        public List<string>? Interests { get; set; }
        public Dictionary<string, string>? PersonaDetails { get; set; }
        public NotificationPrefs? Notifications { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        return profile is null
            ? NotFound(new { error = "Profile not found" })
            : Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateProfileRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        Apply(profile, request);
        await _profiles.SaveProfileAsync(profile);
        return Ok(profile);
    }

    [HttpPost("onboarding-complete")]
    public async Task<IActionResult> CompleteOnboardingAsync([FromBody] UpdateProfileRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        Apply(profile, request);
        await _profiles.CompleteOnboardingAsync(profile);
        return Ok(profile);
    }

    private static void Apply(UserProfile profile, UpdateProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Name))           profile.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.PrimaryGoal))    profile.PrimaryGoal = request.PrimaryGoal.Trim();
        if (request.Region is not null)                          profile.Region = request.Region;
        if (request.City is not null)                            profile.City = request.City;
        if (request.Interests is not null)                       profile.Interests = request.Interests;
        if (request.PersonaDetails is not null)                  profile.PersonaDetails = request.PersonaDetails;
        if (request.Notifications is not null)                   profile.Notifications = request.Notifications;

        if (Enum.TryParse<PersonaType>(request.Persona, ignoreCase: true, out var persona))
            profile.Persona = persona;
    }
}

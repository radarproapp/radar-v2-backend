using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;
using RadarV2.Services;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly IUserProfileService _profiles;
    private readonly IBehavioralSignalService _signals;

    public MeController(IUserProfileService profiles, IBehavioralSignalService signals)
    {
        _profiles = profiles;
        _signals = signals;
    }

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
        public List<UserInterestContext>? InterestContexts { get; set; }
        public List<string>? DominantInterests { get; set; }
        public List<string>? Problems { get; set; }
        public string? CurrentIntent { get; set; }
        public string? TargetRole { get; set; }
        public string? TargetIndustry { get; set; }
        public List<string>? Capabilities { get; set; }
        public List<string>? OpportunityPreferences { get; set; }
        public List<string>? Geography { get; set; }
        public List<string>? DecisionNeeds { get; set; }
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

    [HttpGet("focus")]
    public async Task<IActionResult> GetFocusAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var paths = InterestPathService.Build(profile);
        var active = InterestPathService.ActiveInterests(profile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggestions = profile.InterestActivityScores
            .Where(pair => !active.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .Take(2)
            .Select(pair => pair.Key)
            .ToList();

        return Ok(new { paths, suggestedDominantInterests = suggestions });
    }

    /// <summary>
    /// What Radar has learned about this user: top signals by recency-decayed strength, plus the
    /// overall confidence in the model. Declared and inferred signals are reported separately so the
    /// client can distinguish what the user told us from what we inferred from their behaviour.
    /// </summary>
    [HttpGet("signals")]
    public async Task<IActionResult> GetSignalsAsync([FromQuery] int take = 12)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var all = await _signals.GetSignalsAsync(profile.Id);
        var top = await _signals.GetTopSignalsAsync(profile.Id, Math.Clamp(take, 1, 50));

        return Ok(new
        {
            profileConfidence = await _signals.GetProfileConfidenceAsync(profile.Id),
            declaredCount = all.Count(s => s.Source == SignalSource.Declared),
            inferredCount = all.Count(s => s.Source == SignalSource.Inferred),
            signals = top.Select(s => new
            {
                s.Term,
                s.Source,
                s.Strength,
                s.Confidence,
                s.ExplicitFeedback,
                s.PositiveCount,
                s.NegativeCount,
                s.UpdatedAt,
                decayedStrength = BehavioralSignalService.DecayedStrength(s),
            }),
        });
    }

    /// <summary>
    /// Explicitly suppress a topic. Unlike event-driven feedback this is recorded against every
    /// source that already has a row for the term, so an explicit rejection overrides inference
    /// instead of sitting alongside it.
    /// </summary>
    [HttpDelete("signals")]
    public async Task<IActionResult> RemoveSignalAsync([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return BadRequest(new { error = "A signal 'term' is required." });

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var trimmed = term.Trim();
        var rows = (await _signals.GetSignalsAsync(profile.Id))
            .Where(s => s.Term.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var row in rows)
            await _signals.RecordAsync(profile.Id, row.Term, BehavioralAction.RemoveInterest, row.Source);

        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateProfileRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var previousInterests = profile.Interests.ToList();
        Apply(profile, request);
        await _profiles.SaveProfileAsync(profile);
        await SyncDeclaredSignalsAsync(profile.Id, previousInterests, profile.Interests);
        return Ok(profile);
    }

    [HttpPost("onboarding-complete")]
    public async Task<IActionResult> CompleteOnboardingAsync([FromBody] UpdateProfileRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var previousInterests = profile.Interests.ToList();
        Apply(profile, request);
        await _profiles.CompleteOnboardingAsync(profile);
        await SyncDeclaredSignalsAsync(profile.Id, previousInterests, profile.Interests);
        return Ok(profile);
    }

    /// <summary>
    /// Keeps the persisted signal memory in step with the interests the user states directly.
    /// Added interests become strong declared signals; dropped ones are explicitly suppressed so
    /// they stop resurfacing through inference. Removals only touch terms that already exist, so we
    /// never create a signal purely to zero it.
    /// </summary>
    private async Task SyncDeclaredSignalsAsync(string userId, IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current)
    {
        var added   = current.Where(i => !previous.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        var removed = previous.Where(i => !current.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        if (added.Count == 0 && removed.Count == 0) return;

        foreach (var interest in added)
            await _signals.RecordAsync(userId, interest, BehavioralAction.MoreLikeThis, SignalSource.Declared);

        if (removed.Count == 0) return;

        var rows = await _signals.GetSignalsAsync(userId);
        foreach (var interest in removed)
            foreach (var row in rows.Where(r => r.Term.Equals(interest, StringComparison.OrdinalIgnoreCase)))
                await _signals.RecordAsync(userId, row.Term, BehavioralAction.RemoveInterest, row.Source);
    }

    private static void Apply(UserProfile profile, UpdateProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Name))           profile.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.PrimaryGoal))    profile.PrimaryGoal = request.PrimaryGoal.Trim();
        if (request.Region is not null)                          profile.Region = request.Region;
        if (request.City is not null)                            profile.City = request.City;
        if (request.Interests is not null)
        {
            profile.Interests = request.Interests.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            profile.InterestContexts = (request.InterestContexts ?? profile.InterestContexts)
                .Where(c => profile.Interests.Contains(c.Interest, StringComparer.OrdinalIgnoreCase))
                .GroupBy(c => c.Interest, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var interest in profile.Interests.Where(i => profile.InterestContexts.All(c => !c.Interest.Equals(i, StringComparison.OrdinalIgnoreCase))))
                profile.InterestContexts.Add(new UserInterestContext { Interest = interest, Goal = profile.PrimaryGoal });
        }
        else if (request.InterestContexts is not null)
        {
            profile.InterestContexts = request.InterestContexts;
        }
        if (request.DominantInterests is not null)
            profile.DominantInterests = request.DominantInterests.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (request.Problems is not null)
            profile.Problems = Clean(request.Problems);
        if (!string.IsNullOrWhiteSpace(request.CurrentIntent))   profile.CurrentIntent = request.CurrentIntent.Trim();
        if (!string.IsNullOrWhiteSpace(request.TargetRole))      profile.TargetRole = request.TargetRole.Trim();
        if (!string.IsNullOrWhiteSpace(request.TargetIndustry))  profile.TargetIndustry = request.TargetIndustry.Trim();
        if (request.Capabilities is not null)                    profile.Capabilities = Clean(request.Capabilities);
        if (request.OpportunityPreferences is not null)          profile.OpportunityPreferences = Clean(request.OpportunityPreferences);
        if (request.Geography is not null)                       profile.Geography = Clean(request.Geography);
        if (request.DecisionNeeds is not null)                   profile.DecisionNeeds = Clean(request.DecisionNeeds);

        profile.InterestPaths = InterestPathService.Build(profile);
        if (request.PersonaDetails is not null)                  profile.PersonaDetails = request.PersonaDetails;
        if (request.Notifications is not null)                   profile.Notifications = request.Notifications;

        if (Enum.TryParse<PersonaType>(request.Persona, ignoreCase: true, out var persona))
            profile.Persona = persona;
    }

    private static List<string> Clean(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .Select(v => v.Trim())
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList();
}

using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IUserProfileService
{
    Task<UserProfile?> GetCurrentUserAsync();
    Task SaveProfileAsync(UserProfile profile);
    Task UpdateStatsAsync(UserStats stats);
    Task CompleteOnboardingAsync(UserProfile profile);

    /// <summary>Every profile on the signed-in account, newest last.</summary>
    Task<List<UserProfile>> GetProfilesAsync();

    /// <summary>
    /// Starts a second profile on the same account and makes it the active one.
    /// </summary>
    Task<UserProfile?> CreateProfileAsync(PersonaType persona, string? label, CancellationToken ct = default);

    /// <summary>
    /// Switches which profile the account is using. Returns false if it belongs to someone else.
    /// </summary>
    Task<bool> ActivateProfileAsync(string profileId, CancellationToken ct = default);
}

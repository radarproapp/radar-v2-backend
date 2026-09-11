using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IUserProfileService
{
    Task<UserProfile?> GetCurrentUserAsync();
    Task SaveProfileAsync(UserProfile profile);
    Task UpdateStatsAsync(UserStats stats);
    Task CompleteOnboardingAsync(UserProfile profile);
}

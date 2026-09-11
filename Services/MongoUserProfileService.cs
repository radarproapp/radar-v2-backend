using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoUserProfileService : IUserProfileService
{
    private readonly RadarDatabase _db;
    private readonly IUserSessionService _session;

    public MongoUserProfileService(RadarDatabase db, IUserSessionService session)
    {
        _db = db;
        _session = session;
    }

    public async Task<UserProfile?> GetCurrentUserAsync()
    {
        if (!_session.IsAuthenticated) return null;

        var user = await _db.Users
            .Find(u => u.Id == _session.UserId)
            .FirstOrDefaultAsync();

        if (user == null) return null;

        return await _db.Profiles
            .Find(p => p.Id == user.ProfileId)
            .FirstOrDefaultAsync();
    }

    public async Task SaveProfileAsync(UserProfile profile)
    {
        await _db.Profiles.ReplaceOneAsync(
            p => p.Id == profile.Id,
            profile,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task UpdateStatsAsync(UserStats stats)
    {
        var profile = await GetCurrentUserAsync();
        if (profile == null) return;

        profile.Stats = stats;
        await SaveProfileAsync(profile);
    }

    public async Task CompleteOnboardingAsync(UserProfile profile)
    {
        profile.OnboardingComplete = true;
        await SaveProfileAsync(profile);
    }
}

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

        var profile = await _db.Profiles
            .Find(p => p.Id == user.ProfileId)
            .FirstOrDefaultAsync();

        if (profile is null) return null;

        // Backfilled on read. Profiles written before an account could hold more than one have no
        // owner recorded, and without it they cannot be listed alongside a second profile.
        if (string.IsNullOrEmpty(profile.UserId))
        {
            profile.UserId = user.Id;
            await _db.Profiles.UpdateOneAsync(
                p => p.Id == profile.Id,
                Builders<UserProfile>.Update.Set(p => p.UserId, user.Id));
        }

        return profile;
    }

    public async Task<List<UserProfile>> GetProfilesAsync()
    {
        if (!_session.IsAuthenticated) return [];

        // Reading the current profile first backfills its owner, so an account that has only ever
        // had one profile still returns it here rather than an empty list.
        await GetCurrentUserAsync();

        return await _db.Profiles
            .Find(p => p.UserId == _session.UserId)
            .SortBy(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<UserProfile?> CreateProfileAsync(PersonaType persona, string? label, CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated) return null;

        var user = await _db.Users.Find(u => u.Id == _session.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return null;

        var existing = await GetCurrentUserAsync();

        var profile = new UserProfile
        {
            UserId = user.Id,
            Persona = persona,
            // Named in order to create the profile, so this one is theirs rather than the default.
            PersonaChosenAt = DateTime.UtcNow,
            Label = string.IsNullOrWhiteSpace(label) ? PersonaLabel(persona) : label.Trim(),
            // The name follows the person, not the profile; everything else starts empty, because
            // a graduate profile that inherited a student's interests would be the same guessing
            // the rest of Radar stopped doing.
            Name = existing?.Name ?? string.Empty,
            OnboardingComplete = false,
        };

        await _db.Profiles.InsertOneAsync(profile, cancellationToken: ct);

        await _db.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            Builders<Data.Documents.UserDocument>.Update.Set(u => u.ProfileId, profile.Id),
            cancellationToken: ct);

        return profile;
    }

    public async Task<bool> ActivateProfileAsync(string profileId, CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated || string.IsNullOrWhiteSpace(profileId)) return false;

        // Checked against the owner, not merely that it exists: switching must not be a way to
        // read somebody else's profile by guessing an id.
        var profile = await _db.Profiles
            .Find(p => p.Id == profileId && p.UserId == _session.UserId)
            .FirstOrDefaultAsync(ct);

        if (profile is null) return false;

        await _db.Users.UpdateOneAsync(
            u => u.Id == _session.UserId,
            Builders<Data.Documents.UserDocument>.Update.Set(u => u.ProfileId, profileId),
            cancellationToken: ct);

        return true;
    }

    private static string PersonaLabel(PersonaType persona) => persona switch
    {
        PersonaType.Student => "Student",
        PersonaType.Graduate => "Graduate",
        PersonaType.YoungProfessional => "Professional",
        PersonaType.Entrepreneur => "Founder",
        PersonaType.Researcher => "Researcher",
        _ => "Profile",
    };

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

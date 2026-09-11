using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class AuthService : IAuthService
{
    private readonly RadarDatabase _db;

    public AuthService(RadarDatabase db) => _db = db;

    public async Task<AuthResult> RegisterAsync(string name, string email, string password)
    {
        var existing = await _db.Users
            .Find(u => u.Email == email.ToLowerInvariant())
            .FirstOrDefaultAsync();

        if (existing != null)
            return new AuthResult { Success = false, Error = "An account with this email already exists." };

        var profileId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        var user = new UserDocument
        {
            Id = userId,
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            ProfileId = profileId,
            CreatedAt = DateTime.UtcNow
        };

        var profile = new UserProfile
        {
            Id = profileId,
            Name = name,
            Email = email.ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow
        };

        await _db.Users.InsertOneAsync(user);
        await _db.Profiles.InsertOneAsync(profile);

        return new AuthResult { Success = true, UserId = userId, UserName = name };
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        var user = await _db.Users
            .Find(u => u.Email == email.ToLowerInvariant())
            .FirstOrDefaultAsync();

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return new AuthResult { Success = false, Error = "Invalid email or password." };

        var profile = await _db.Profiles
            .Find(p => p.Id == user.ProfileId)
            .FirstOrDefaultAsync();

        return new AuthResult
        {
            Success = true,
            UserId = user.Id,
            UserName = profile?.Name ?? email
        };
    }
}

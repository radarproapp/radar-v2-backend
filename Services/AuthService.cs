using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class AuthService : IAuthService
{
    private readonly RadarDatabase _db;
    private readonly IConfiguration _config;

    public AuthService(RadarDatabase db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResult> RegisterAsync(string name, string email, string password)
    {
        var existing = await _db.Users
            .Find(u => u.Email == email.ToLowerInvariant())
            .FirstOrDefaultAsync();

        if (existing != null)
            return new AuthResult { Success = false, Error = "An account with this email already exists." };

        var profileId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var role = ResolveRole(email);

        var user = new UserDocument
        {
            Id = userId,
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            ProfileId = profileId,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        var profile = new UserProfile
        {
            Id = profileId,
            Name = name,
            Email = email.ToLowerInvariant(),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        await _db.Users.InsertOneAsync(user);
        await _db.Profiles.InsertOneAsync(profile);

        return new AuthResult { Success = true, UserId = userId, UserName = name, Role = role };
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

        var role = ResolveRole(email, user.Role);
        if (profile is not null && profile.Role != role)
        {
            profile.Role = role;
            await _db.Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);
        }

        return new AuthResult
        {
            Success = true,
            UserId = user.Id,
            UserName = profile?.Name ?? email,
            Role = role
        };
    }

    private string ResolveRole(string email, string existingRole = "User")
    {
        var normalized = email.Trim().ToLowerInvariant();
        var superAdmins = _config.GetSection("Auth:SuperAdminEmails").Get<string[]>() ?? [];
        if (superAdmins.Any(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase))) return "SuperAdmin";

        var admins = _config.GetSection("Auth:AdminEmails").Get<string[]>() ?? [];
        if (admins.Any(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase))) return "PlatformAdmin";

        return existingRole is "SuperAdmin" or "PlatformAdmin" ? existingRole : "User";
    }
}

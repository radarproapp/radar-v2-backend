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
    private readonly IEmailSender _email;
    private readonly ILogger<AuthService> _log;

    public AuthService(
        RadarDatabase db,
        IConfiguration config,
        IEmailSender email,
        ILogger<AuthService> log)
    {
        _db = db;
        _config = config;
        _email = email;
        _log = log;
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

        await SendWelcomeAsync(name, user.Email);

        return new AuthResult { Success = true, UserId = userId, UserName = name, Role = role };
    }

    /// <summary>
    /// The one message a new account genuinely needs.
    /// </summary>
    /// <remarks>
    /// Not a tour and not a feature list. It does two things worth an email: it proves the
    /// address was typed correctly, which is how somebody finds out before they are locked out of
    /// an account they cannot reset — and it tells them the one thing the product will not do for
    /// them, which is guess what they want to follow.
    ///
    /// Awaited rather than fired and forgotten, but never allowed to fail a registration: an
    /// account that exists is worth more than a greeting, and a sender that is down must not stop
    /// somebody signing up.
    /// </remarks>
    private async Task SendWelcomeAsync(string name, string email)
    {
        try
        {
            var site = PublicSite.From(_config);
            var firstName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

            var (html, text) = EmailTemplate.Build(
                heading: $"Your Radar account is ready, {firstName}",
                preheader: "Radar stays empty until you say what you are following.",
                paragraphs:
                [
                    "Radar starts empty on purpose. It does not read your degree and decide what you should care about — it asks, and until it has an answer there is nothing it can honestly rank.",
                    "Three questions set it up: what you follow, what you are working towards, and what you want it to watch for. After that the feed is yours, and you can change any of it later.",
                    "Keep this message. If you ever need to get back in, a reset link goes to this address.",
                ],
                action: new EmailTemplate.Action_("Set up your feed", $"{site}/personalise"),
                footnote: "You are getting this because an account was created with this address. If that was not you, reply and tell us.",
                publicUrl: site);

            await _email.SendAsync(email, "Your Radar account is ready", text, html);
        }
        catch (Exception ex)
        {
            // A greeting is not worth failing a registration for.
            _log.LogWarning(ex, "Could not send a welcome email.");
        }
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
            Role = role,

            // Whatever the account is on now. Signing in after a reset has to produce a token the
            // reset does not immediately invalidate.
            TokenVersion = user.TokenVersion
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

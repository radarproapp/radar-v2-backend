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
    private readonly AdminRoleService _roles;
    private readonly EmailVerificationService _verification;
    private readonly ILogger<AuthService> _log;

    public AuthService(
        RadarDatabase db,
        IConfiguration config,
        IEmailSender email,
        AdminRoleService roles,
        EmailVerificationService verification,
        ILogger<AuthService> log)
    {
        _db = db;
        _config = config;
        _email = email;
        _roles = roles;
        _verification = verification;
        _log = log;
    }

    /// <summary>
    /// Creates an account, attributing it to a referral code when one actually arrived with it.
    /// </summary>
    /// <remarks>
    /// <paramref name="referralCode"/> is whatever the sign-up form carried, in whatever shape
    /// the person typed it. A code that matches nobody is ignored rather than refused: a wrong
    /// code is somebody mistyping a friend's link, and losing their sign-up over it would cost
    /// the thing the scheme exists to produce.
    /// </remarks>
    /// <summary>
    /// The participant whose code this is, or null.
    /// </summary>
    /// <remarks>
    /// Null for a code nobody holds, and null for no code at all. There is deliberately no
    /// fallback: an account that arrives without a working code is unattributed, and stays
    /// that way. Guessing would put somebody else's sign-ups on a leaderboard.
    /// </remarks>
    /// <summary>
    /// Whose code this is, if it is anybody's.
    /// </summary>
    /// <param name="newProfileId">
    /// The account about to be created. Passed so a code cannot credit its own owner: nobody can
    /// reach that today, because the profile does not exist yet when this runs and nothing ever
    /// writes an attribution afterwards - but a profile whose ReferredByUserId equals its own id
    /// was measured returning "signed up 1, came back 1" and appearing on its own board row, and
    /// that is too cheap a check to leave to the ordering of two statements staying as it is.
    /// </param>
    private async Task<UserProfile?> FindReferrerAsync(string? referralCode, string newProfileId)
    {
        var code = ReferralCodes.Normalise(referralCode);
        if (code is null) return null;

        var referrer = await _db.Profiles.Find(p => p.ReferralCode == code).FirstOrDefaultAsync();

        return referrer?.Id == newProfileId ? null : referrer;
    }

    public async Task<AuthResult> RegisterAsync(string name, string email, string password, string? referralCode = null)
    {
        // Ordinary accounts only. An admin login on the same address is a different row, and a
        // person who runs Radar is entitled to use it too - refusing their sign-up because they
        // already hold admin would be the opposite of what the two accounts are for.
        var existing = await _db.Users
            .Find(u => u.Email == email.ToLowerInvariant() && u.Kind == null)
            .FirstOrDefaultAsync();

        if (existing != null)
            return new AuthResult { Success = false, Error = "An account with this email already exists." };

        var profileId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var role = await ResolveRoleAsync(email);

        var user = new UserDocument
        {
            Id = userId,
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            ProfileId = profileId,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        // Who brought them, if anybody did.
        //
        // Resolved before the profile is written so attribution is part of creating the
        // account rather than a second write that can fail on its own - a sign-up that is
        // attributed to nobody because the follow-up update threw is a participant's work
        // disappearing silently.
        var referrer = await FindReferrerAsync(referralCode, profileId);

        var profile = new UserProfile
        {
            Id = profileId,
            Name = name,
            Email = email.ToLowerInvariant(),
            Role = role,
            CreatedAt = DateTime.UtcNow,
            ReferredByUserId = referrer?.Id,

            // The referrer's own code, not the input parsed a second time. The same value today,
            // and it cannot drift from the account it was matched against.
            ReferredByCode = referrer?.ReferralCode,

            ReferredAt = referrer is null ? null : DateTime.UtcNow,
        };

        await _db.Users.InsertOneAsync(user);
        await _db.Profiles.InsertOneAsync(profile);

        await SendWelcomeAsync(name, user.Email);

        // And the one that asks this address to answer. Fire and forget in the same sense the
        // welcome is: a registration is not worth failing because mail is down, and somebody who
        // is never asked simply has a referral that does not count until they ask again.
        await _verification.SendAsync(userId);

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

    /// <summary>
    /// Signs in to one kind of account.
    /// </summary>
    /// <param name="adminAccount">
    /// Which row to look for. The two logins must not reach each other: /login finding an admin
    /// row would hand somebody's ordinary session the admin role, and /admin/login finding an
    /// ordinary row would undo the separation the two accounts exist to create.
    /// </param>
    public async Task<AuthResult> LoginAsync(string email, string password, bool adminAccount = false)
    {
        var kind = adminAccount ? UserDocument.AdminKind : null;

        var user = await _db.Users
            .Find(u => u.Email == email.ToLowerInvariant() && u.Kind == kind)
            .FirstOrDefaultAsync();

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return new AuthResult { Success = false, Error = "Invalid email or password." };

        var profile = await _db.Profiles
            .Find(p => p.Id == user.ProfileId)
            .FirstOrDefaultAsync();

        var role = await ResolveRoleAsync(email);

        if (profile is not null && profile.Role != role)
        {
            profile.Role = role;
            await _db.Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);
        }

        // The account row as well as the profile. Only the profile was being brought into line,
        // so the role stored beside the password drifted from the one the product acted on - and
        // it is the row an admin screen reads when it wants to know who holds what.
        if (user.Role != role)
        {
            user.Role = role;
            await _db.Users.UpdateOneAsync(
                u => u.Id == user.Id,
                Builders<UserDocument>.Update.Set(u => u.Role, role));
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

    /// <summary>
    /// The role this address is entitled to, from configuration and nothing else.
    /// </summary>
    /// <remarks>
    /// This used to fall back to the role already stored on the account, so a role survived its
    /// address being removed forever and the only way to take it back was editing MongoDB by
    /// hand. Taking it away now demotes the account the next time it signs in - and
    /// <see cref="SessionVersionGuard"/> refuses the admin token it is already holding, so it
    /// does not have to wait for that.
    ///
    /// Through <see cref="AdminRoleService"/> rather than straight at the configuration, so a
    /// person invited from inside the product gets the role too - and so this and the guard
    /// cannot disagree about who is an admin.
    /// </remarks>
    private Task<string> ResolveRoleAsync(string email) => _roles.RoleForAsync(email);
}

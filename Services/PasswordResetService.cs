using System.Security.Cryptography;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// Issuing and redeeming a password reset.
/// </summary>
/// <remarks>
/// There was no reset at all — a user who mistyped a password at signup had no route back into
/// their account, which on a phone keyboard is not a rare event.
///
/// Three rules this follows, all of them about not leaking who has an account:
///
/// <list type="bullet">
/// <item>Requesting a reset for an unknown address succeeds exactly as loudly as one for a known
/// address. An error saying "no such user" is an account enumeration oracle.</item>
/// <item>The token is random, single-use and short-lived, and only its hash is stored. A leaked
/// database should not hand over working reset links.</item>
/// <item>Redeeming reports only whether it worked, never why it did not.</item>
/// </list>
///
/// And one about what a reset is for: redeeming ends every session already open on the account.
/// Somebody resetting a password they believe has been seen is trying to remove whoever has it,
/// and a thirty-day token that keeps working would have left them there for a month while the
/// screen said the password had been changed.
/// </remarks>
public class PasswordResetService
{
    private readonly RadarDatabase _db;
    private readonly IEmailSender _email;
    private readonly SessionVersionGuard _sessions;
    private readonly ILogger<PasswordResetService> _log;

    public PasswordResetService(
        RadarDatabase db,
        IEmailSender email,
        SessionVersionGuard sessions,
        ILogger<PasswordResetService> log)
    {
        _db = db;
        _email = email;
        _sessions = sessions;
        _log = log;
    }

    /// <summary>
    /// Long enough that guessing is not a strategy, short enough to retype from an email if the
    /// link is mangled by a client.
    /// </summary>
    private const int TokenBytes = 32;

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Issues a reset if the address belongs to an account. Returns nothing either way.
    /// </summary>
    public async Task RequestAsync(string email, string resetUrlBase, CancellationToken ct = default)
    {
        var normalised = email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Find(u => u.Email.ToLower() == normalised)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            // Deliberately silent to the caller, and logged without the address so the log is not
            // itself a list of people who tried.
            _log.LogInformation("Password reset requested for an address with no account.");
            return;
        }

        // Any outstanding tokens for this user stop working. Asking again should not leave two
        // live links, and a user who asks twice because the first did not arrive expects the
        // second to be the one that counts.
        await _db.PasswordResets.DeleteManyAsync(r => r.UserId == user.Id, ct);

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenBytes));

        await _db.PasswordResets.InsertOneAsync(new PasswordResetDoc
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        }, cancellationToken: ct);

        var site = resetUrlBase.TrimEnd('/');
        var link = $"{site}/reset-password?token={token}";

        // The copy says what happened, what the link does, how long it lasts and what to do if it
        // was not them. No greeting, no sign-off, no exclamation mark: somebody who cannot get
        // into their account wants the link and the facts about it.
        var (html, text) = EmailTemplate.Build(
            heading: "Choose a new password",
            preheader: "The link works once and expires in an hour.",
            paragraphs:
            [
                "Someone asked to reset the password for this Radar account.",
                "The link below works once and expires in an hour.",
            ],
            action: new EmailTemplate.Action_("Set a new password", link),
            footnote: "If this was not you, ignore this message. Nothing has changed, and the link expires on its own.",
            publicUrl: site);

        await _email.SendAsync(user.Email, "Reset your Radar password", text, html, ct);
    }

    /// <summary>
    /// Redeems a token and sets the new password. False when the token is unknown, used or
    /// expired — without saying which.
    /// </summary>
    public async Task<bool> RedeemAsync(string token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || newPassword.Length < 8) return false;

        var hash = Hash(token);

        var reset = await _db.PasswordResets
            .Find(r => r.TokenHash == hash)
            .FirstOrDefaultAsync(ct);

        if (reset is null || reset.ExpiresAt < DateTime.UtcNow)
        {
            // An expired row is removed on the way out rather than left to accumulate.
            if (reset is not null) await _db.PasswordResets.DeleteOneAsync(r => r.Id == reset.Id, ct);
            return false;
        }

        var user = await _db.Users.Find(u => u.Id == reset.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return false;

        await _db.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            BuildReset(BCrypt.Net.BCrypt.HashPassword(newPassword)),
            cancellationToken: ct);

        // The new version is whatever the database arrived at, which this does not know — so the
        // cached one is dropped rather than guessed.
        _sessions.Forget(user.Id);

        // Single use: consumed whether or not anything else later fails.
        await _db.PasswordResets.DeleteManyAsync(r => r.UserId == user.Id, ct);

        _log.LogInformation("A password was reset and the account's existing sessions were ended.");
        return true;
    }

    /// <summary>
    /// The write a redemption performs: the new password, and the end of every session.
    /// </summary>
    /// <remarks>
    /// One write, both changes. The second half is what makes a reset mean anything: without it
    /// the new password governs only the next sign-in, and every token already issued keeps
    /// working for up to thirty days — so the one action somebody takes when they think their
    /// account has been seen would not remove whoever had seen it.
    ///
    /// Combined rather than sequential because the halves are worthless apart. A new password
    /// without the sessions ended is the defect this fixes; sessions ended without the new
    /// password is an account its owner cannot get into.
    ///
    /// An update rather than writing back the user object that was read a moment earlier: a
    /// replace would carry the version from before this change and undo a reset running beside it.
    /// </remarks>
    public static UpdateDefinition<UserDocument> BuildReset(string passwordHash) =>
        Builders<UserDocument>.Update.Combine(
            Builders<UserDocument>.Update.Set(u => u.PasswordHash, passwordHash),
            SessionVersionGuard.EndAllSessions());

    /// <summary>Only the hash is stored, so the table is not a list of working reset links.</summary>
    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}

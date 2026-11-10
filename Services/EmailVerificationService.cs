using System.Security.Cryptography;
using System.Text;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// Asking an address to answer, and recording that it did.
/// </summary>
/// <remarks>
/// This exists because of a prize. A referral count is only worth what the accounts behind it
/// are worth, and nothing stopped somebody creating thirty accounts on addresses they invented -
/// which made "came back", the strongest of the three counts, manufacturable with two days of
/// patience. The one cheap thing that stops it is asking the address to answer.
///
/// It gates nothing about using Radar. Somebody who never opens the email still reads their
/// feed: making a product unusable until mail arrives loses the people whose mail is slow for
/// reasons that are not their fault, and this is not a security boundary - it is a quality bar
/// on a number.
///
/// The token is shaped like a password reset - hashed at rest, single use, time limited -
/// because the same reasoning applies: a leaked copy of this collection must not be a set of
/// working links.
/// </remarks>
public class EmailVerificationService
{
    private const int TokenBytes = 32;

    /// <summary>
    /// How long a link lasts.
    /// </summary>
    /// <remarks>
    /// Days rather than the hour a reset gets. A reset answers somebody waiting at the screen;
    /// this arrives while they are getting on with the thing they signed up for, and one that
    /// expires before they next open their email just makes them ask for another.
    /// </remarks>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly RadarDatabase _db;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailVerificationService> _log;

    public EmailVerificationService(
        RadarDatabase db,
        IEmailSender email,
        IConfiguration config,
        ILogger<EmailVerificationService> log)
    {
        _db = db;
        _email = email;
        _config = config;
        _log = log;
    }

    /// <summary>What happened when somebody followed a link.</summary>
    public enum VerifyResult { Verified, AlreadyVerified, UnknownOrExpired }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Sends the link, replacing any outstanding one.
    /// </summary>
    /// <remarks>
    /// Returns false when it could not be sent rather than throwing. A registration is not worth
    /// failing over an email: somebody who cannot be told has an account that works and a
    /// referral that does not count yet, which they can fix by asking again.
    ///
    /// Already-verified addresses are not re-sent to, so a resend button cannot be used to post
    /// mail at somebody repeatedly once they have answered.
    /// </remarks>
    public async Task<bool> SendAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null || user.IsEmailVerified) return false;
        if (!_email.IsConfigured) return false;

        // Any outstanding link stops working. Asking twice should not leave two live links, and
        // whoever asks expects the newest mail to be the one that works.
        await _db.EmailVerifications.DeleteManyAsync(v => v.UserId == userId, ct);

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenBytes));

        await _db.EmailVerifications.InsertOneAsync(new EmailVerificationDoc
        {
            UserId = userId,
            TokenHash = Hash(token),
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        }, cancellationToken: ct);

        var site = PublicSite.From(_config);

        var (html, text) = EmailTemplate.Build(
            heading: "Confirm your email address",
            preheader: "One tap, and your Radar account is confirmed.",
            paragraphs:
            [
                "Confirming tells us this address reaches you. Radar works either way - nothing is locked until you do this.",
                "It matters if somebody introduced you: a referral only counts once the address behind it has answered, which is how the campaign keeps its numbers honest.",
            ],
            action: new EmailTemplate.Action_("Confirm this address", $"{site}/verify-email?token={token}"),
            footnote: "If you did not create a Radar account, ignore this. The link expires on its own and nothing happens until somebody follows it.",
            publicUrl: site);

        return await _email.SendAsync(user.Email, "Confirm your Radar email address", text, html, ct);
    }

    /// <summary>Records that an address answered.</summary>
    public async Task<VerifyResult> VerifyAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return VerifyResult.UnknownOrExpired;

        var hash = Hash(token);
        var request = await _db.EmailVerifications.Find(v => v.TokenHash == hash).FirstOrDefaultAsync(ct);

        if (request is null || request.ExpiresAt < DateTime.UtcNow)
        {
            // An expired row is cleared on the way out rather than left to accumulate.
            if (request is not null) await _db.EmailVerifications.DeleteOneAsync(v => v.Id == request.Id, ct);
            return VerifyResult.UnknownOrExpired;
        }

        var user = await _db.Users.Find(u => u.Id == request.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return VerifyResult.UnknownOrExpired;

        // Said apart from a failure, because following the same link twice is the ordinary thing
        // a person does when they are not sure the first tap worked. Telling them it is invalid
        // would send them to ask for another one they do not need.
        if (user.IsEmailVerified)
        {
            await _db.EmailVerifications.DeleteManyAsync(v => v.UserId == user.Id, ct);
            return VerifyResult.AlreadyVerified;
        }

        await _db.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            Builders<UserDocument>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow),
            cancellationToken: ct);

        // Single use, consumed whether or not anything later fails.
        await _db.EmailVerifications.DeleteManyAsync(v => v.UserId == user.Id, ct);

        _log.LogInformation("An email address was confirmed.");
        return VerifyResult.Verified;
    }

    /// <summary>Whether this deployment can ask anybody anything.</summary>
    public bool CanEmail => _email.IsConfigured;
}

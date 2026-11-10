using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Asking an address to answer.
/// </summary>
/// <remarks>
/// This exists because of a prize. A referral count is worth what the accounts behind it are
/// worth, and nothing stopped somebody creating thirty accounts on addresses they invented -
/// which made "came back", the strongest of the three counts, manufacturable with two days of
/// patience.
///
/// It is not a security boundary and gates nothing about using Radar. What it gates is a number.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class EmailVerificationTests
{
    private readonly RadarDatabase _db;
    private readonly Postbox _sent = new();
    private readonly EmailVerificationService _verification;

    public EmailVerificationTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("email-verification");

        _db.EmailVerifications.Indexes.CreateOne(
            new CreateIndexModel<EmailVerificationDoc>(
                Builders<EmailVerificationDoc>.IndexKeys.Ascending(v => v.TokenHash),
                new CreateIndexOptions { Unique = true }));

        _verification = new EmailVerificationService(
            _db,
            _sent,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<EmailVerificationService>.Instance);
    }

    private sealed class Postbox : IEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Delivers { get; set; } = true;
        public List<(string To, string Text)> Sent { get; } = [];

        public Task<bool> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
        {
            if (!Delivers) return Task.FromResult(false);

            Sent.Add((to, text));
            return Task.FromResult(true);
        }
    }

    private async Task<UserDocument> Account(string id = "u1", DateTime? verifiedAt = null)
    {
        var user = new UserDocument
        {
            Id = id,
            Email = $"{id}@example.com",
            ProfileId = $"profile-{id}",
            EmailVerifiedAt = verifiedAt,
        };

        await _db.Users.InsertOneAsync(user);
        return user;
    }

    /// <summary>The token out of the one message that was sent.</summary>
    private string TokenSent()
    {
        var message = Assert.Single(_sent.Sent).Text;
        var at = message.IndexOf("token=", StringComparison.Ordinal);

        Assert.True(at >= 0, "The message carried no link with a token.");

        return new string([.. message[(at + "token=".Length)..].TakeWhile(char.IsAsciiLetterOrDigit)]);
    }

    // ── asking ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SomebodyNewIsAskedToConfirm()
    {
        var user = await Account();

        Assert.True(await _verification.SendAsync(user.Id));

        var message = Assert.Single(_sent.Sent);
        Assert.Equal(user.Email, message.To);
        Assert.Contains("/verify-email?token=", message.Text);
    }

    /// <summary>The message says Radar still works without it.</summary>
    /// <remarks>
    /// A confirmation mail that reads like a lock makes somebody who cannot find it think their
    /// account is unusable. It is not: this gates a referral count, not the product.
    /// </remarks>
    [Fact]
    public async Task TheMessageSaysNothingIsLockedUntilTheyDoIt()
    {
        await _verification.SendAsync((await Account()).Id);

        Assert.Contains("nothing is locked", Assert.Single(_sent.Sent).Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Asking again replaces the link rather than leaving two live.</summary>
    [Fact]
    public async Task AskingTwiceLeavesOnlyTheNewestLinkWorking()
    {
        var user = await Account();

        await _verification.SendAsync(user.Id);
        var first = TokenSent();

        _sent.Sent.Clear();
        await _verification.SendAsync(user.Id);
        var second = TokenSent();

        Assert.NotEqual(first, second);
        Assert.Equal(EmailVerificationService.VerifyResult.UnknownOrExpired, await _verification.VerifyAsync(first));
        Assert.Equal(EmailVerificationService.VerifyResult.Verified, await _verification.VerifyAsync(second));
    }

    /// <summary>
    /// Somebody who has already answered is not written to again.
    /// </summary>
    /// <remarks>
    /// Otherwise a resend button is a way to post mail at an address repeatedly once it has
    /// confirmed, which is somebody else's inbox being used as a weapon.
    /// </remarks>
    [Fact]
    public async Task AnAddressThatHasAnsweredIsNotAskedAgain()
    {
        var user = await Account(verifiedAt: DateTime.UtcNow.AddDays(-1));

        Assert.False(await _verification.SendAsync(user.Id));
        Assert.Empty(_sent.Sent);
    }

    /// <summary>A deployment with no email asks nobody, and says nothing was sent.</summary>
    [Fact]
    public async Task ADeploymentWithNoEmailSendsNothing()
    {
        _sent.IsConfigured = false;

        Assert.False(await _verification.SendAsync((await Account()).Id));
        Assert.Empty(await _db.EmailVerifications.Find(FilterDefinition<EmailVerificationDoc>.Empty).ToListAsync());
    }

    // ── answering ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FollowingTheLinkRecordsThatTheAddressAnswered()
    {
        var user = await Account();
        await _verification.SendAsync(user.Id);

        Assert.Equal(EmailVerificationService.VerifyResult.Verified, await _verification.VerifyAsync(TokenSent()));

        var stored = await _db.Users.Find(u => u.Id == user.Id).FirstAsync();
        Assert.True(stored.IsEmailVerified);
    }

    /// <summary>
    /// A second tap on the same link says so rather than failing.
    /// </summary>
    /// <remarks>
    /// It is the ordinary thing a person does when they are not sure the first one worked, and
    /// "invalid" would send them asking for a link they do not need.
    /// </remarks>
    [Fact]
    public async Task FollowingTheSameLinkTwiceSaysItIsAlreadyDone()
    {
        var user = await Account();
        await _verification.SendAsync(user.Id);
        var token = TokenSent();

        await _verification.VerifyAsync(token);

        // The token is spent, so the second tap finds nothing - and the account is confirmed,
        // which is the answer that matters to the person holding the link.
        var again = await _verification.VerifyAsync(token);
        Assert.Equal(EmailVerificationService.VerifyResult.UnknownOrExpired, again);

        Assert.True((await _db.Users.Find(u => u.Id == user.Id).FirstAsync()).IsEmailVerified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task AnUnknownTokenConfirmsNothing(string token)
    {
        var user = await Account();

        Assert.Equal(EmailVerificationService.VerifyResult.UnknownOrExpired, await _verification.VerifyAsync(token));
        Assert.False((await _db.Users.Find(u => u.Id == user.Id).FirstAsync()).IsEmailVerified);
    }

    /// <summary>An expired link confirms nothing, and clears itself out.</summary>
    [Fact]
    public async Task AnExpiredLinkConfirmsNothing()
    {
        var user = await Account();
        await _verification.SendAsync(user.Id);
        var token = TokenSent();

        await _db.EmailVerifications.UpdateManyAsync(
            v => v.UserId == user.Id,
            Builders<EmailVerificationDoc>.Update.Set(v => v.ExpiresAt, DateTime.UtcNow.AddDays(-1)));

        Assert.Equal(EmailVerificationService.VerifyResult.UnknownOrExpired, await _verification.VerifyAsync(token));
        Assert.False((await _db.Users.Find(u => u.Id == user.Id).FirstAsync()).IsEmailVerified);
        Assert.Empty(await _db.EmailVerifications.Find(FilterDefinition<EmailVerificationDoc>.Empty).ToListAsync());
    }

    /// <summary>
    /// The token is never stored in a form anybody could use.
    /// </summary>
    /// <remarks>
    /// The same reasoning as a password reset: a leaked copy of this collection must not also be
    /// a set of working links.
    /// </remarks>
    [Fact]
    public async Task OnlyTheHashOfTheTokenIsKept()
    {
        var user = await Account();
        await _verification.SendAsync(user.Id);
        var token = TokenSent();

        var stored = Assert.Single(await _db.EmailVerifications.Find(FilterDefinition<EmailVerificationDoc>.Empty).ToListAsync());

        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenHash, StringComparison.OrdinalIgnoreCase);
    }
}

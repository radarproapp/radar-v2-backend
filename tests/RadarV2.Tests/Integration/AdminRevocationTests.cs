using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Taking admin access back, from somebody who is already holding a token.
/// </summary>
/// <remarks>
/// Removing an address from the admin lists demoted the account at its next sign-in. That is no
/// use against the case the feature exists for: somebody whose access is being withdrawn has no
/// reason to sign in again, and a Radar token lasts thirty days. They kept full admin - including
/// the super admin view of one named person's reading history - for the rest of the month.
///
/// Against a real mongod, because the check reads the account to find the address the token does
/// not carry, and the caching around that read is half of what makes it safe to do per request.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class AdminRevocationTests
{
    private readonly RadarDatabase _db;

    public AdminRevocationTests(RadarDatabaseFixture fixture) => _db = fixture.Database("admin-revocation");

    private static ClaimsPrincipal Token(string userId, string role, int version = 0) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
            new Claim(SessionVersionGuard.ClaimName, version.ToString()),
        ]));

    private SessionVersionGuard Guard(params string[] superAdmins)
    {
        var values = superAdmins
            .Select((email, index) => (email, index))
            .ToDictionary(entry => $"Auth:SuperAdminEmails:{entry.index}", entry => (string?)entry.email);

        return new SessionVersionGuard(
            _db,
            new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            NullLogger<SessionVersionGuard>.Instance);
    }

    private async Task<UserDocument> Account(string id, string email, string role, int tokenVersion = 0)
    {
        var user = new UserDocument
        {
            Id = id,
            Email = email,
            Role = role,
            ProfileId = $"profile-{id}",
            TokenVersion = tokenVersion,
        };

        await _db.Users.InsertOneAsync(user);
        return user;
    }

    [Fact]
    public async Task AConfiguredAdminIsStillServed()
    {
        await Account("u1", "owner@radar.com", "SuperAdmin");

        var verdict = await Guard("owner@radar.com").CheckAsync(Token("u1", "SuperAdmin"));

        Assert.True(verdict.Ok);
    }

    /// <summary>
    /// The token stops working the moment the address comes out of the configuration.
    /// </summary>
    /// <remarks>
    /// Not at its next sign-in, which would never come. This is the defect the whole change
    /// exists for.
    /// </remarks>
    [Fact]
    public async Task ATokenClaimingAdminIsRefusedOnceTheAddressIsRemoved()
    {
        await Account("u1", "former@radar.com", "SuperAdmin");

        // Configured with somebody else, which is what removing an address looks like.
        var verdict = await Guard("owner@radar.com").CheckAsync(Token("u1", "SuperAdmin"));

        Assert.False(verdict.Ok);
        Assert.Contains("no longer has admin access", verdict.Reason);
    }

    [Fact]
    public async Task ATokenClaimingAdminIsRefusedWhenNoAdminIsConfiguredAtAll()
    {
        await Account("u1", "former@radar.com", "SuperAdmin");

        Assert.False((await Guard().CheckAsync(Token("u1", "SuperAdmin"))).Ok);
    }

    /// <summary>
    /// An ordinary user's token is never touched by this.
    /// </summary>
    /// <remarks>
    /// The check only ever takes access away. A token claiming User stays valid whatever the
    /// lists say - including when the address has just been granted admin, because widening a
    /// live session's powers from a background configuration read is not something this should
    /// be able to do. A new admin signs in again and gets a token that says so.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryUsersTokenIsLeftAlone()
    {
        await Account("u1", "reader@radar.com", "User");

        Assert.True((await Guard().CheckAsync(Token("u1", "User"))).Ok);
        Assert.True((await Guard("reader@radar.com").CheckAsync(Token("u1", "User"))).Ok);
    }

    /// <summary>A promotion does not sign the lesser token out.</summary>
    /// <remarks>
    /// Somebody moved from the admin list to the super admin list is holding a PlatformAdmin
    /// token. Refusing it would sign out the person who had just been given more access.
    /// </remarks>
    [Fact]
    public async Task APlatformAdminTokenSurvivesBeingPromoted()
    {
        await Account("u1", "staff@radar.com", "PlatformAdmin");

        Assert.True((await Guard("staff@radar.com").CheckAsync(Token("u1", "PlatformAdmin"))).Ok);
    }

    /// <summary>The password-reset check still works, and reports its own reason.</summary>
    [Fact]
    public async Task ATokenIssuedBeforeAPasswordResetIsStillRefused()
    {
        await Account("u1", "owner@radar.com", "SuperAdmin", tokenVersion: 3);

        var verdict = await Guard("owner@radar.com").CheckAsync(Token("u1", "SuperAdmin", version: 1));

        Assert.False(verdict.Ok);
        Assert.Contains("password was changed", verdict.Reason);
    }

    /// <summary>
    /// An account row that is not there is left alone rather than locked out.
    /// </summary>
    /// <remarks>
    /// Nothing in Radar deletes a user row, so the only way here is a manual delete - and
    /// inventing a lockout for a case the product cannot reach makes a transient miss look like
    /// a compromise. The same reasoning the version check already used.
    /// </remarks>
    [Fact]
    public async Task AMissingAccountIsNotLockedOut()
    {
        var verdict = await Guard("owner@radar.com").CheckAsync(Token("nobody", "SuperAdmin"));

        Assert.True(verdict.Ok);
        await Task.CompletedTask;
    }

    /// <summary>A request with no identity at all is not this check's business.</summary>
    [Fact]
    public async Task AnUnauthenticatedRequestPassesThrough() =>
        Assert.True((await Guard().CheckAsync(null)).Ok);
}

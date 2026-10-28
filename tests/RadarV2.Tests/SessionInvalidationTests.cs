using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RadarV2.Controllers;
using RadarV2.Data.Documents;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Tests;

/// <summary>
/// A password reset has to end the sessions that are already open.
///
/// It did not. A token is a signed assertion rather than a row, nothing on the server was
/// consulted, and Radar's tokens last thirty days — so resetting a password told the user their
/// account was secured while leaving whoever already held a token signed in for a month. These
/// cover the three places that silently undo the fix: the stamp not reaching the token, the
/// increment not reaching the write, and the comparison being drawn too tight.
/// </summary>
public class SessionInvalidationTests
{
    /// <summary>
    /// The deploy itself must not sign anybody out. Every token in circulation when this shipped
    /// carries no stamp at all, and every account starts at zero — so no stamp has to read as
    /// zero, which passes.
    /// </summary>
    [Fact]
    public void ATokenWithNoStampCountsAsZero()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")]));

        Assert.Equal(0, SessionVersionGuard.VersionOf(principal));
        Assert.True(SessionVersionGuard.Accepts(
            SessionVersionGuard.VersionOf(principal), current: 0));
    }

    [Fact]
    public void AStampIsReadBack()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SessionVersionGuard.ClaimName, "4")]));

        Assert.Equal(4, SessionVersionGuard.VersionOf(principal));
    }

    /// <summary>The whole point: a token from before the reset stops being accepted.</summary>
    [Fact]
    public void ATokenFromBeforeAResetIsRefused()
    {
        Assert.False(SessionVersionGuard.Accepts(presented: 0, current: 1));
        Assert.False(SessionVersionGuard.Accepts(presented: 2, current: 5));
    }

    /// <summary>
    /// And the other half, which is easier to get wrong: somebody who has just reset and signed in
    /// holds a stamp ahead of what a second instance has cached, and must not be bounced out of
    /// the account they are recovering.
    /// </summary>
    [Fact]
    public void ATokenFromAfterAResetIsAccepted()
    {
        Assert.True(SessionVersionGuard.Accepts(presented: 1, current: 1));
        Assert.True(SessionVersionGuard.Accepts(presented: 2, current: 1));
    }

    /// <summary>
    /// The reset's write carries both halves. Separating them is the regression this guards: a
    /// password change that lands without the increment leaves every existing session alive, and
    /// nothing visible from outside would say so.
    /// </summary>
    [Fact]
    public void RedeemingWritesTheNewPasswordAndEndsTheSessions()
    {
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = PasswordResetService.BuildReset("a-bcrypt-hash")
            .Render(new RenderArgs<UserDocument>(
                registry.GetSerializer<UserDocument>(), registry));

        Assert.Equal("a-bcrypt-hash", rendered["$set"]["PasswordHash"].AsString);
        Assert.Equal(1, rendered["$inc"]["TokenVersion"].AsInt32);
    }

    /// <summary>
    /// Incremented, never assigned. Two resets landing together both have to count, and a write
    /// that sets a number it read first would have one of them overwrite the other with the same
    /// value — leaving the first reset's sessions alive.
    /// </summary>
    [Fact]
    public void TheVersionIsIncrementedRatherThanAssigned()
    {
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = SessionVersionGuard.EndAllSessions()
            .Render(new RenderArgs<UserDocument>(
                registry.GetSerializer<UserDocument>(), registry));

        Assert.True(rendered.AsBsonDocument.Contains("$inc"));
        Assert.False(rendered.AsBsonDocument.Contains("$set"));
    }

    /// <summary>
    /// A new account starts at zero rather than at something that would refuse its own first
    /// token.
    /// </summary>
    [Fact]
    public void AnAccountStartsAtZero()
    {
        Assert.Equal(0, new UserDocument().TokenVersion);
    }

    /// <summary>
    /// The stamp actually reaches the token. Everything else here would still pass if the claim
    /// were never issued, and the version would then be checked against a stamp nobody writes —
    /// refusing every token the moment any account was reset.
    /// </summary>
    [Fact]
    public void AnIssuedTokenCarriesTheVersionFromTheAccount()
    {
        var token = Issue(new AuthResult
        {
            Success = true,
            UserId = "user-1",
            UserName = "Ada",
            Role = "User",
            TokenVersion = 3,
        });

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var stamped = parsed.Claims.FirstOrDefault(c => c.Type == SessionVersionGuard.ClaimName);

        Assert.NotNull(stamped);
        Assert.Equal("3", stamped!.Value);
    }

    /// <summary>
    /// And it is the account's version, not a constant. A hard-coded zero in the issuer would make
    /// every token look pre-reset the moment anybody reset anything.
    /// </summary>
    [Fact]
    public void TheVersionInATokenIsNotHardCoded()
    {
        var first = Stamp(Issue(Result(1)));
        var second = Stamp(Issue(Result(7)));

        Assert.Equal("1", first);
        Assert.Equal("7", second);
    }

    /// <summary>
    /// Through real validation, not just a decode.
    /// </summary>
    /// <remarks>
    /// Reading a token back shows the claims as written; validating it is what the request path
    /// actually does, and that applies an inbound map which renames several well-known claims on
    /// the way through. If the stamp were ever given a name on that list it would arrive under a
    /// different one, <see cref="SessionVersionGuard.VersionOf"/> would find nothing, every token
    /// would read as zero, and the check would quietly stop working — passing every other test
    /// here while doing so.
    /// </remarks>
    [Fact]
    public void TheStampSurvivesValidationUnderTheNameItIsCheckedBy()
    {
        var token = Issue(Result(3));

        var principal = new JwtSecurityTokenHandler().ValidateToken(
            token,
            new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(SigningKey)),
            },
            out _);

        Assert.Equal(3, SessionVersionGuard.VersionOf(principal));

        // And the id the version is looked up by comes through under the type the guard reads.
        Assert.Equal("user-1", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    private const string SigningKey = "a-test-signing-key-long-enough-for-hmac-sha256-0123456789";

    private static AuthResult Result(int version) => new()
    {
        Success = true,
        UserId = "user-1",
        UserName = "Ada",
        Role = "User",
        TokenVersion = version,
    };

    private static string? Stamp(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .FirstOrDefault(c => c.Type == SessionVersionGuard.ClaimName)?.Value;

    /// <summary>
    /// Mints a token the way the controller does. Through the real method, so a change to how
    /// tokens are built is a change this sees.
    /// </summary>
    private static string Issue(AuthResult result)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Jwt:Key"] = SigningKey,
            })
            .Build();

        // Only the configuration is touched by the token path; the rest would be a mock that
        // asserts nothing.
        var controller = new AuthController(null!, config, null!, null!);

        var build = typeof(AuthController).GetMethod(
            "BuildToken", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(build);

        return (string)build!.Invoke(controller,
        [
            result.UserId!, result.UserName!, result.Role, result.TokenVersion,
            DateTime.UtcNow.AddDays(30),
        ])!;
    }
}

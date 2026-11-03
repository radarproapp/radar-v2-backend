using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Who is an admin, and - the part that did not work - who stops being one.
/// </summary>
/// <remarks>
/// Admin access comes from two lists of addresses in configuration. Granting it worked. Taking
/// it back did not: the resolver fell through to the role already stored on the account, so an
/// address removed from the configuration left the account an admin forever and the only way to
/// undo it was editing MongoDB by hand.
///
/// A permission you can give and cannot take away is not a permission system, and this is the
/// permission that reaches one named person's reading history.
/// </remarks>
public class AdminRoleTests
{
    private static IConfiguration Config(string[]? superAdmins = null, string[]? admins = null)
    {
        var values = new Dictionary<string, string?>();

        foreach (var (email, index) in (superAdmins ?? []).Select((email, index) => (email, index)))
            values[$"Auth:SuperAdminEmails:{index}"] = email;

        foreach (var (email, index) in (admins ?? []).Select((email, index) => (email, index)))
            values[$"Auth:AdminEmails:{index}"] = email;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // ── granting ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnAddressInNeitherListIsNotAnAdmin() =>
        Assert.Equal("User", AdminRoles.For(Config(), "somebody@example.com"));

    [Fact]
    public void TheSuperAdminListGrantsSuperAdmin() =>
        Assert.Equal("SuperAdmin", AdminRoles.For(Config(superAdmins: ["owner@radar.com"]), "owner@radar.com"));

    [Fact]
    public void TheAdminListGrantsPlatformAdmin() =>
        Assert.Equal("PlatformAdmin", AdminRoles.For(Config(admins: ["staff@radar.com"]), "staff@radar.com"));

    /// <summary>Case and surrounding space are how an address gets typed, not a different person.</summary>
    [Theory]
    [InlineData("Owner@Radar.com")]
    [InlineData("  owner@radar.com  ")]
    [InlineData("OWNER@RADAR.COM")]
    public void AnAddressIsMatchedHoweverItWasCased(string typed) =>
        Assert.Equal("SuperAdmin", AdminRoles.For(Config(superAdmins: ["owner@radar.com"]), typed));

    /// <summary>
    /// In both lists means the higher role.
    /// </summary>
    /// <remarks>
    /// The alternative is that adding somebody to the wrong list quietly demotes them, which is a
    /// surprising way for a deployment change to behave.
    /// </remarks>
    [Fact]
    public void AnAddressInBothListsGetsTheHigherRole() =>
        Assert.Equal("SuperAdmin", AdminRoles.For(
            Config(superAdmins: ["owner@radar.com"], admins: ["owner@radar.com"]), "owner@radar.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoAddressIsNotAnAdmin(string? email) =>
        Assert.Equal("User", AdminRoles.For(Config(superAdmins: ["owner@radar.com"]), email));

    /// <summary>A blank entry in the configuration does not grant anybody anything.</summary>
    /// <remarks>
    /// An env var set to an empty string is the ordinary way a deployment ends up with one, and
    /// an empty entry matching an empty email would have handed out super admin.
    /// </remarks>
    [Fact]
    public void AnEmptyEntryInTheListGrantsNothing()
    {
        var config = Config(superAdmins: ["", "   "]);

        Assert.Equal("User", AdminRoles.For(config, ""));
        Assert.Equal("User", AdminRoles.For(config, "   "));
        Assert.Empty(AdminRoles.Configured(config));
    }

    // ── taking it back ───────────────────────────────────────────────────────

    /// <summary>
    /// Removing an address removes the role.
    /// </summary>
    /// <remarks>
    /// This is the whole point. The resolver used to end with "if they are already an admin,
    /// leave them one", so this returned SuperAdmin for an address no longer in any list.
    /// </remarks>
    [Fact]
    public void RemovingAnAddressFromTheListsTakesTheRoleBack() =>
        Assert.Equal("User", AdminRoles.For(Config(superAdmins: ["somebody-else@radar.com"]), "former@radar.com"));

    // ── the team screen ──────────────────────────────────────────────────────

    [Fact]
    public void TheConfiguredTeamNamesEveryAddressAndItsRole()
    {
        var team = AdminRoles.Configured(Config(
            superAdmins: ["owner@radar.com"],
            admins: ["staff@radar.com", "intern@radar.com"])).ToList();

        Assert.Equal(("owner@radar.com", "SuperAdmin"), team[0]);
        Assert.Contains(("staff@radar.com", "PlatformAdmin"), team);
        Assert.Contains(("intern@radar.com", "PlatformAdmin"), team);
        Assert.Equal(3, team.Count);
    }

    /// <summary>One person, one row, holding the role they actually have.</summary>
    [Fact]
    public void SomebodyInBothListsAppearsOnceAsASuperAdmin()
    {
        var team = AdminRoles.Configured(Config(
            superAdmins: ["owner@radar.com"],
            admins: ["owner@radar.com"])).ToList();

        Assert.Equal(("owner@radar.com", "SuperAdmin"), Assert.Single(team));
    }

    /// <summary>No admin configured at all is the state a fresh deployment starts in.</summary>
    [Fact]
    public void AnUnconfiguredDeploymentHasNoAdmins()
    {
        Assert.Empty(AdminRoles.Configured(Config()));
        Assert.Equal("User", AdminRoles.For(Config(), "anybody@radar.com"));
    }

    // ── what counts as an admin role ─────────────────────────────────────────

    [Theory]
    [InlineData("SuperAdmin", true)]
    [InlineData("PlatformAdmin", true)]
    [InlineData("User", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheTwoAdminRolesCountAsAdmin(string? role, bool isAdmin) =>
        Assert.Equal(isAdmin, AdminRoles.IsAdmin(role));

    /// <summary>
    /// The claim on the token is what authorisation reads, so that is what is checked.
    /// </summary>
    /// <remarks>
    /// Asserted here as a principal rather than as a string, because the defect being guarded
    /// against is a token claiming a role the configuration has since withdrawn - and the stored
    /// role on the account is updated at sign-in, which is precisely the moment that will not
    /// come for somebody who has no reason to sign in again.
    /// </remarks>
    [Fact]
    public void AnAdminClaimIsRecognisedOnAPrincipal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")]));

        Assert.True(AdminRoles.IsAdmin(principal.FindFirst(ClaimTypes.Role)?.Value));
    }
}

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Inviting somebody into the admin area, and taking it back.
/// </summary>
/// <remarks>
/// Two places can grant admin access and they are not equal. The deployment's configuration is
/// the floor: nothing inside the product can edit it, so there is always a way back in when this
/// collection is wrong. Grants made here sit on top and can be removed by a super admin.
///
/// The rules worth protecting are the ones that stop the set emptying or quietly widening -
/// nobody removes their own access, nobody removes a configured admin, and a grant never lowers
/// what a deploy already said.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class AdminInviteTests
{
    private readonly RadarDatabase _db;
    private readonly AdminRoleService _roles;

    public AdminInviteTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("admin-invites");

        _db.AdminGrants.Indexes.CreateOne(
            new CreateIndexModel<AdminGrant>(
                Builders<AdminGrant>.IndexKeys.Ascending(g => g.Email),
                new CreateIndexOptions { Unique = true }));

        _roles = Service("owner@radar.com");
    }

    /// <summary>A service whose configuration lists these addresses as super admins.</summary>
    private AdminRoleService Service(params string[] configuredSuperAdmins)
    {
        var values = configuredSuperAdmins
            .Select((email, index) => (email, index))
            .ToDictionary(entry => $"Auth:SuperAdminEmails:{entry.index}", entry => (string?)entry.email);

        return new AdminRoleService(
            _db,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    // ── inviting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnInvitedAddressBecomesAnAdmin()
    {
        Assert.Equal(AdminRoleService.GrantResult.Granted,
            await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com"));

        Assert.Equal(AdminRoles.PlatformAdmin, await _roles.RoleForAsync("staff@radar.com"));
    }

    /// <summary>The invite waits for an account rather than needing one.</summary>
    /// <remarks>
    /// The person being invited usually does not have a Radar account yet. Keying the grant on
    /// an address is what lets the role be waiting for them when they sign up.
    /// </remarks>
    [Fact]
    public async Task AnAddressWithNoAccountCanStillBeInvited()
    {
        await _roles.GrantAsync("nobody-yet@radar.com", AdminRoles.SuperAdmin, "owner@radar.com");

        Assert.Equal(AdminRoles.SuperAdmin, await _roles.RoleForAsync("nobody-yet@radar.com"));
        Assert.Empty(await _db.Users.Find(FilterDefinition<Data.Documents.UserDocument>.Empty).ToListAsync());
    }

    [Theory]
    [InlineData("Staff@Radar.com")]
    [InlineData("  staff@radar.com ")]
    public async Task AnAddressIsMatchedHoweverItWasTyped(string typed)
    {
        await _roles.GrantAsync(typed, AdminRoles.PlatformAdmin, "owner@radar.com");

        Assert.Equal(AdminRoles.PlatformAdmin, await _roles.RoleForAsync("staff@radar.com"));
        Assert.Single(await _roles.TeamAsync(), member => member.Email == "staff@radar.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("two@@radar.com")]
    [InlineData("trailing@radar.")]
    [InlineData("has space@radar.com")]
    public async Task SomethingThatIsNotAnAddressIsRefused(string typed)
    {
        Assert.Equal(AdminRoleService.GrantResult.NotAnEmail,
            await _roles.GrantAsync(typed, AdminRoles.PlatformAdmin, "owner@radar.com"));

        Assert.Empty(await _db.AdminGrants.Find(FilterDefinition<AdminGrant>.Empty).ToListAsync());
    }

    /// <summary>Only the two real roles, never a defaulted one.</summary>
    [Theory]
    [InlineData("User")]
    [InlineData("Owner")]
    [InlineData("")]
    public async Task AnythingThatIsNotOneOfTheTwoRolesIsRefused(string role)
    {
        Assert.Equal(AdminRoleService.GrantResult.NotARole,
            await _roles.GrantAsync("staff@radar.com", role, "owner@radar.com"));

        Assert.Equal(AdminRoles.User, await _roles.RoleForAsync("staff@radar.com"));
    }

    /// <summary>Inviting twice changes the role rather than making a second row.</summary>
    [Fact]
    public async Task InvitingSomebodyAgainChangesTheirRole()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        await _roles.GrantAsync("staff@radar.com", AdminRoles.SuperAdmin, "owner@radar.com");

        Assert.Equal(AdminRoles.SuperAdmin, await _roles.RoleForAsync("staff@radar.com"));
        Assert.Single(await _db.AdminGrants.Find(FilterDefinition<AdminGrant>.Empty).ToListAsync());
    }

    /// <summary>Who let them in is recorded, by address.</summary>
    [Fact]
    public async Task TheGrantRecordsWhoMadeIt()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "Owner@Radar.com");

        var member = Assert.Single(await _roles.TeamAsync(), m => m.Email == "staff@radar.com");

        Assert.Equal("owner@radar.com", member.GrantedByEmail);
        Assert.NotNull(member.GrantedAt);
        Assert.False(member.FromConfiguration);
    }

    // ── revoking ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokingTakesTheRoleBack()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        Assert.Equal(AdminRoleService.RevokeResult.Revoked,
            await _roles.RevokeAsync("staff@radar.com", "owner@radar.com"));

        Assert.Equal(AdminRoles.User, await _roles.RoleForAsync("staff@radar.com"));
        Assert.DoesNotContain(await _roles.TeamAsync(), member => member.Email == "staff@radar.com");
    }

    /// <summary>Revoking keeps the record of what happened.</summary>
    /// <remarks>
    /// Who had access and who took it away is the part worth having later, and a deleted row
    /// answers neither.
    /// </remarks>
    [Fact]
    public async Task ARevokedGrantIsKeptRatherThanDeleted()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        await _roles.RevokeAsync("staff@radar.com", "owner@radar.com");

        var stored = Assert.Single(await _db.AdminGrants.Find(FilterDefinition<AdminGrant>.Empty).ToListAsync());

        Assert.False(stored.IsActive);
        Assert.Equal("owner@radar.com", stored.RevokedByEmail);
        Assert.NotNull(stored.RevokedAt);
    }

    /// <summary>Somebody revoked can be invited back.</summary>
    [Fact]
    public async Task AnAddressCanBeInvitedAgainAfterBeingRevoked()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        await _roles.RevokeAsync("staff@radar.com", "owner@radar.com");
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        Assert.Equal(AdminRoles.PlatformAdmin, await _roles.RoleForAsync("staff@radar.com"));
    }

    [Fact]
    public async Task RevokingAnAddressThatWasNeverGrantedSaysSo() =>
        Assert.Equal(AdminRoleService.RevokeResult.NotGranted,
            await _roles.RevokeAsync("stranger@radar.com", "owner@radar.com"));

    // ── the rules that stop the set emptying ─────────────────────────────────

    /// <summary>
    /// Nobody removes their own access.
    /// </summary>
    /// <remarks>
    /// Not politeness. Every caller is already an admin, so refusing this means the set can never
    /// empty - and a deployment with no configured admin and nobody granted is one nobody can
    /// get into without a deploy.
    /// </remarks>
    [Fact]
    public async Task NobodyCanRemoveTheirOwnAccess()
    {
        var roles = Service();
        await roles.GrantAsync("staff@radar.com", AdminRoles.SuperAdmin, "staff@radar.com");

        Assert.Equal(AdminRoleService.RevokeResult.CannotRevokeYourself,
            await roles.RevokeAsync("staff@radar.com", "Staff@Radar.com"));

        Assert.Equal(AdminRoles.SuperAdmin, await roles.RoleForAsync("staff@radar.com"));
    }

    /// <summary>
    /// A configured admin cannot be removed from inside the product.
    /// </summary>
    /// <remarks>
    /// The configured lists are the way back in when this collection is wrong. A product whose
    /// every route to admin can be closed from inside it has no floor, and somebody who got one
    /// super admin account could lock the owner out of their own deployment.
    /// </remarks>
    [Fact]
    public async Task AConfiguredAdminCannotBeRemovedHere()
    {
        Assert.Equal(AdminRoleService.RevokeResult.GrantedByConfiguration,
            await _roles.RevokeAsync("owner@radar.com", "somebody-else@radar.com"));

        Assert.Equal(AdminRoles.SuperAdmin, await _roles.RoleForAsync("owner@radar.com"));
    }

    /// <summary>A grant cannot lower what the configuration already said.</summary>
    /// <remarks>
    /// Otherwise "invite" would be a way to demote the owner to the lesser role, which is a
    /// quieter version of locking them out.
    /// </remarks>
    [Fact]
    public async Task AGrantCannotReduceWhatTheConfigurationGranted()
    {
        Assert.Equal(AdminRoleService.GrantResult.AlreadyHasIt,
            await _roles.GrantAsync("owner@radar.com", AdminRoles.PlatformAdmin, "somebody-else@radar.com"));

        Assert.Equal(AdminRoles.SuperAdmin, await _roles.RoleForAsync("owner@radar.com"));
    }

    // ── the team screen ──────────────────────────────────────────────────────

    /// <summary>Configured rows first, and each row says where it came from.</summary>
    [Fact]
    public async Task TheTeamNamesBothSourcesAndKeepsThemApart()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        var team = await _roles.TeamAsync();

        Assert.Equal("owner@radar.com", team[0].Email);
        Assert.True(team[0].FromConfiguration);
        Assert.Equal("staff@radar.com", team[1].Email);
        Assert.False(team[1].FromConfiguration);
    }

    /// <summary>Somebody granted in both places appears once, as the configured row.</summary>
    [Fact]
    public async Task AnAddressInBothPlacesIsListedOnce()
    {
        await _db.AdminGrants.InsertOneAsync(new AdminGrant
        {
            Email = "owner@radar.com",
            Role = AdminRoles.PlatformAdmin,
            GrantedByEmail = "somebody@radar.com",
        });

        var owner = Assert.Single(await _roles.TeamAsync(), member => member.Email == "owner@radar.com");

        Assert.True(owner.FromConfiguration);
        Assert.Equal(AdminRoles.SuperAdmin, owner.Role);
    }

    /// <summary>A deployment with nothing configured and nobody invited has no admins.</summary>
    [Fact]
    public async Task AnUnconfiguredDeploymentWithNoInvitesHasNobody()
    {
        var roles = Service();

        Assert.Empty(await roles.TeamAsync());
        Assert.Equal(AdminRoles.User, await roles.RoleForAsync("anybody@radar.com"));
    }
}

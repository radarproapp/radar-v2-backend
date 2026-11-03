using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
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
    private readonly Postbox _sent = new();

    public AdminInviteTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("admin-invites");

        _db.AdminGrants.Indexes.CreateOne(
            new CreateIndexModel<AdminGrant>(
                Builders<AdminGrant>.IndexKeys.Ascending(g => g.Email),
                new CreateIndexOptions { Unique = true }));

        _roles = Service("owner@radar.com");
    }

    /// <summary>
    /// A sender that keeps what it was asked to send, and can be told to fail.
    /// </summary>
    /// <remarks>
    /// The real one returns false rather than throwing, which is the case worth testing here: an
    /// invite that cannot be delivered must not leave access behind it.
    /// </remarks>
    private sealed class Postbox : IEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Delivers { get; set; } = true;
        public List<(string To, string Subject, string Text)> Sent { get; } = [];

        public Task<bool> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
        {
            if (!Delivers) return Task.FromResult(false);

            Sent.Add((to, subject, text));
            return Task.FromResult(true);
        }
    }

    /// <summary>A service whose configuration lists these addresses as super admins.</summary>
    private AdminRoleService Service(params string[] configuredSuperAdmins) =>
        new(_db, Config(configuredSuperAdmins), new MemoryCache(new MemoryCacheOptions()), _sent);

    private static IConfiguration Config(params string[] configuredSuperAdmins)
    {
        var values = configuredSuperAdmins
            .Select((email, index) => (email, index))
            .ToDictionary(entry => $"Auth:SuperAdminEmails:{entry.index}", entry => (string?)entry.email);

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>The invitation side: the token, the message, and the account it creates.</summary>
    private AdminInviteService Invites(params string[] configuredSuperAdmins)
    {
        var config = Config(configuredSuperAdmins);

        return new AdminInviteService(
            _db,
            new AdminRoleService(_db, config, new MemoryCache(new MemoryCacheOptions()), _sent),
            _sent,
            config,
            NullLogger<AdminInviteService>.Instance);
    }

    /// <summary>The token out of the one message that was sent.</summary>
    private string TokenSent()
    {
        var message = Assert.Single(_sent.Sent).Text;
        var marker = "token=";
        var at = message.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(at >= 0, "The message carried no link with a token.");

        var rest = message[(at + marker.Length)..];
        return new string([.. rest.TakeWhile(char.IsAsciiLetterOrDigit)]);
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

    // -- the invitation ------------------------------------------------------

    /// <summary>An invited person is sent a link, and told what it is for.</summary>
    [Fact]
    public async Task AnInvitedPersonIsSentALink()
    {
        Assert.Equal(AdminInviteService.InviteResult.Sent,
            await Invites("owner@radar.com").InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com"));

        var message = Assert.Single(_sent.Sent);

        Assert.Equal("staff@radar.com", message.To);
        Assert.Contains("Platform Admin", message.Text);
        Assert.Contains("/admin/accept?token=", message.Text);
    }

    /// <summary>
    /// The message does not name whoever sent it.
    /// </summary>
    /// <remarks>
    /// It used to, in three places - the preheader, the opening line and the footnote - as a way
    /// for the reader to tell a real invitation from a phishing attempt. The cost was a staff
    /// address in every invitation, including ones a typo sent to a stranger. Recorded and shown
    /// on the admin team screen instead, where only admins see it.
    /// </remarks>
    [Fact]
    public async Task TheMessageDoesNotNameWhoInvitedThem()
    {
        await Invites("owner@radar.com").InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        var message = Assert.Single(_sent.Sent);

        Assert.DoesNotContain("owner@radar.com", message.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner@radar.com", message.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>It is still recorded, for the people entitled to know.</summary>
    [Fact]
    public async Task WhoInvitedThemIsStillKeptForTheTeamScreen()
    {
        await Invites("owner@radar.com").InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        var invite = Assert.Single(await _db.AdminInvites.Find(FilterDefinition<AdminInviteDoc>.Empty).ToListAsync());
        Assert.Equal("owner@radar.com", invite.InvitedByEmail);

        var member = Assert.Single(await _roles.TeamAsync(), m => m.Email == "staff@radar.com");
        Assert.Equal("owner@radar.com", member.GrantedByEmail);
    }

    /// <summary>The message says the admin account is a separate one with its own password.</summary>
    [Fact]
    public async Task TheMessageSaysItIsASeparateAccount()
    {
        await Invites("owner@radar.com").InviteAsync("staff@radar.com", AdminRoles.SuperAdmin, "owner@radar.com");

        var message = Assert.Single(_sent.Sent).Text;

        Assert.Contains("separate account", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("own password", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Setting a password creates an admin account, not a role on an existing one.
    /// </summary>
    /// <remarks>
    /// The person's ordinary Radar account is a different row on the same address and is left
    /// exactly as it was - same password, same profile, still just a reader.
    /// </remarks>
    [Fact]
    public async Task AcceptingCreatesASeparateAdminAccount()
    {
        await _db.Users.InsertOneAsync(new UserDocument
        {
            Id = "reader",
            Email = "staff@radar.com",
            PasswordHash = "their-reader-password",
            ProfileId = "profile-1",
        });

        var invites = Invites("owner@radar.com");
        await invites.InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        Assert.Equal(AdminInviteService.AcceptResult.Created,
            await invites.AcceptAsync(TokenSent(), "a-real-password"));

        var rows = await _db.Users.Find(u => u.Email == "staff@radar.com").ToListAsync();
        Assert.Equal(2, rows.Count);

        var admin = Assert.Single(rows, row => row.IsAdminAccount);
        Assert.Equal(AdminRoles.PlatformAdmin, admin.Role);
        Assert.Empty(admin.ProfileId);

        // Their ordinary account is untouched.
        var reader = Assert.Single(rows, row => !row.IsAdminAccount);
        Assert.Equal("their-reader-password", reader.PasswordHash);
        Assert.Equal("profile-1", reader.ProfileId);
    }

    /// <summary>An admin account has no profile, so it never counts as a Radar user.</summary>
    [Fact]
    public async Task AnAdminAccountHasNoProfile()
    {
        var invites = Invites("owner@radar.com");
        await invites.InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        await invites.AcceptAsync(TokenSent(), "a-real-password");

        Assert.Empty(await _db.Profiles.Find(FilterDefinition<UserProfile>.Empty).ToListAsync());
    }

    /// <summary>The link works once.</summary>
    [Fact]
    public async Task AnInvitationCannotBeUsedTwice()
    {
        var invites = Invites("owner@radar.com");
        await invites.InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        var token = TokenSent();
        await invites.AcceptAsync(token, "a-real-password");

        Assert.Equal(AdminInviteService.AcceptResult.AlreadyAccepted,
            await invites.AcceptAsync(token, "a-different-password"));

        Assert.Single(await _db.Users.Find(u => u.Kind == UserDocument.AdminKind).ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task AnUnknownTokenCreatesNothing(string token)
    {
        Assert.Equal(AdminInviteService.AcceptResult.UnknownOrExpired,
            await Invites().AcceptAsync(token, "a-real-password"));

        Assert.Empty(await _db.Users.Find(FilterDefinition<UserDocument>.Empty).ToListAsync());
    }

    [Fact]
    public async Task AShortPasswordIsRefused()
    {
        var invites = Invites("owner@radar.com");
        await invites.InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");

        Assert.Equal(AdminInviteService.AcceptResult.PasswordTooShort,
            await invites.AcceptAsync(TokenSent(), "short"));

        Assert.Empty(await _db.Users.Find(FilterDefinition<UserDocument>.Empty).ToListAsync());
    }

    /// <summary>
    /// An invitation that sat in an inbox while the access was revoked hands nothing back.
    /// </summary>
    /// <remarks>
    /// The role is read when the link is redeemed, not when it was written. Otherwise removing
    /// somebody would leave a live link that undoes it.
    /// </remarks>
    [Fact]
    public async Task AnInvitationDoesNotSurviveTheAccessBeingRevoked()
    {
        var invites = Invites("owner@radar.com");
        await invites.InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        var token = TokenSent();

        await Service("owner@radar.com").RevokeAsync("staff@radar.com", "owner@radar.com");

        Assert.Equal(AdminInviteService.AcceptResult.UnknownOrExpired,
            await invites.AcceptAsync(token, "a-real-password"));

        Assert.Empty(await _db.Users.Find(FilterDefinition<UserDocument>.Empty).ToListAsync());
    }

    /// <summary>
    /// A message that could not be sent leaves nothing behind it.
    /// </summary>
    /// <remarks>
    /// The invitation is the message. An address granted access and never told has a row on a
    /// screen and no idea it exists, which is indistinguishable from a typo.
    /// </remarks>
    [Fact]
    public async Task AFailedSendLeavesNoInvitationAndNoAccess()
    {
        _sent.Delivers = false;

        Assert.Equal(AdminInviteService.InviteResult.CouldNotSend,
            await Invites("owner@radar.com").InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com"));

        Assert.Empty(await _db.AdminInvites.Find(FilterDefinition<AdminInviteDoc>.Empty).ToListAsync());
        Assert.Empty(await _db.AdminGrants.Find(FilterDefinition<AdminGrant>.Empty).ToListAsync());
        Assert.Equal(AdminRoles.User, await _roles.RoleForAsync("staff@radar.com"));
    }

    /// <summary>A deployment with no email cannot invite, and says so rather than failing oddly.</summary>
    [Fact]
    public async Task ADeploymentWithNoEmailCannotInvite()
    {
        _sent.IsConfigured = false;

        Assert.Equal(AdminInviteService.InviteResult.EmailNotConfigured,
            await Invites("owner@radar.com").InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com"));

        Assert.Empty(await _db.AdminInvites.Find(FilterDefinition<AdminInviteDoc>.Empty).ToListAsync());
    }

    /// <summary>
    /// Somebody who already has an admin account is not sent a new set-a-password link.
    /// </summary>
    /// <remarks>
    /// Otherwise inviting an existing admin would be a way to take their account over.
    /// </remarks>
    [Fact]
    public async Task SomebodyWhoAlreadyHasAnAdminAccountIsNotInvitedAgain()
    {
        var invites = Invites("owner@radar.com");
        await invites.InviteAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        await invites.AcceptAsync(TokenSent(), "a-real-password");
        _sent.Sent.Clear();

        Assert.Equal(AdminInviteService.InviteResult.AlreadyHasAnAdminAccount,
            await invites.InviteAsync("staff@radar.com", AdminRoles.SuperAdmin, "owner@radar.com"));

        Assert.Empty(_sent.Sent);
    }

    /// <summary>A refused invitation sends nothing at all.</summary>
    [Theory]
    [InlineData("not-an-address", AdminRoles.PlatformAdmin)]
    [InlineData("staff@radar.com", "User")]
    [InlineData("owner@radar.com", AdminRoles.PlatformAdmin)]
    public async Task NothingIsSentForAnInvitationThatWasRefused(string email, string role)
    {
        await Invites("owner@radar.com").InviteAsync(email, role, "somebody-else@radar.com");

        Assert.Empty(_sent.Sent);
        Assert.Empty(await _db.AdminInvites.Find(FilterDefinition<AdminInviteDoc>.Empty).ToListAsync());
    }

    /// <summary>Revoking tells nobody, which is deliberate.</summary>
    /// <remarks>
    /// Somebody whose access was removed finds out by it not being there, and a notification
    /// they cannot act on is noise.
    /// </remarks>
    [Fact]
    public async Task RevokingSendsNothing()
    {
        await _roles.GrantAsync("staff@radar.com", AdminRoles.PlatformAdmin, "owner@radar.com");
        _sent.Sent.Clear();

        await _roles.RevokeAsync("staff@radar.com", "owner@radar.com");

        Assert.Empty(_sent.Sent);
    }
}

using EphemeralMongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace RadarV2.Tests.Http;

/// <summary>
/// The real application, over real HTTP, against a real mongod.
/// </summary>
/// <remarks>
/// Everything in this suite until now called a service directly, so every claim about who may
/// reach an endpoint was read off an attribute rather than asserted. That is the half of the
/// system authorisation actually lives in: the role on a token, the order of UseAuthentication
/// and UseAuthorization, whether a method-level [Authorize] narrows the controller's or replaces
/// it, and what a refusal looks like from outside.
///
/// None of that is visible from a unit test, and all of it is one attribute away from being
/// wrong in a way nothing would catch - the kind of mistake that hands every signed-in user the
/// review queue with every existing test still green.
/// </remarks>
public sealed class RadarAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IMongoRunner _mongo;

    /// <summary>An address the configuration grants super admin, for the break-glass path.</summary>
    public const string ConfiguredSuperAdmin = "owner@radar-test.com";

    /// <summary>An address the configuration grants the lesser role.</summary>
    public const string ConfiguredPlatformAdmin = "staff@radar-test.com";

    public RadarAppFixture()
    {
        _mongo = MongoRunner.Run(new MongoRunnerOptions { UseSingleNodeReplicaSet = false });

        // Environment variables, not ConfigureAppConfiguration.
        //
        // Program.cs reads the signing key into a local the moment it runs, and a
        // WebApplicationFactory's configuration delegates are applied after that - so an override
        // there is seen by anything reading IConfiguration per request and NOT by the startup
        // read. The result is a token signed with one key and validated against another: every
        // authenticated request comes back 401, and it reads like a broken role attribute rather
        // than a configuration that arrived too late. Environment variables are already in the
        // builder before the first line of Program.cs.
        //
        // Set rather than appended, and never cleared between classes: the whole suite wants the
        // same answers here, and the database name is unique per fixture.
        Set("MongoDB__ConnectionString", _mongo.ConnectionString);
        Set("MongoDB__DatabaseName", $"radar-http-{Guid.NewGuid():N}");

        // Long enough for HMAC-SHA256, and nothing like the real one.
        Set("Auth__Jwt__Key", "a-signing-key-used-only-by-the-http-tests-0123456789");
        Set("Auth__Jwt__Issuer", "RadarTest");
        Set("Auth__Jwt__Audience", "RadarTest");

        Set("Auth__SuperAdminEmails__0", ConfiguredSuperAdmin);
        Set("Auth__AdminEmails__0", ConfiguredPlatformAdmin);
    }

    private static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, not the default. Production refuses to start on the checked-in signing
        // key, which is correct of it and not what this is here to test.
        builder.UseEnvironment(Environments.Development);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        _mongo.Dispose();
    }
}

/// <summary>
/// Shared by every HTTP test class, so the application and mongod start once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RadarAppCollection : ICollectionFixture<RadarAppFixture>
{
    public const string Name = "http";
}

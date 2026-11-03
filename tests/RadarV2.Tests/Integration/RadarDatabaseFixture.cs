using MongoDB.Driver;
using EphemeralMongo;
using Microsoft.Extensions.Configuration;
using RadarV2.Data;

namespace RadarV2.Tests.Integration;

/// <summary>
/// A real MongoDB, started for the test run and thrown away after it.
/// </summary>
/// <remarks>
/// Everything in this suite until now was a pure function, because there was nothing to run a
/// query against. That left the half of the system that is queries completely uncovered, and
/// three of the defects found in review lived exactly there: a filter that used $nin, which
/// matches a missing field, where the predicate beside it read the model's default and said the
/// opposite; a type filter that replaced a surface instead of narrowing it; a sort with no index
/// over a collection the window was taken from. None of those are visible from a unit test,
/// because a unit test never asks the database anything.
///
/// A real server rather than a hand-written fake. A fake agrees with whatever its author believed
/// the driver does, which is the belief being tested.
///
/// One server for the whole run, a fresh database per test class. Starting mongod costs a second
/// or two; doing it per test would cost a minute.
/// </remarks>
public sealed class RadarDatabaseFixture : IDisposable
{
    private readonly IMongoRunner _runner;

    public RadarDatabaseFixture()
    {
        _runner = MongoRunner.Run(new MongoRunnerOptions
        {
            UseSingleNodeReplicaSet = false,
        });

        _client = new Lazy<IMongoClient>(() => new MongoClient(_runner.ConnectionString));
    }

    /// <summary>
    /// One client for the whole run, shared by every database handed out.
    /// </summary>
    /// <remarks>
    /// A MongoClient is a connection pool, forty sockets wide by default, and
    /// <see cref="RadarDatabase"/>'s ordinary constructor opens one per instance. xUnit builds
    /// the test class once per test case, so the suite was opening a pool per test and asking
    /// one mongod for several thousand connections - which it refused, failing twenty-three
    /// unrelated tests with "Timed out waiting for a connection" the moment the suite grew past
    /// the threshold. One client, one pool, and the failures are about the code again.
    /// </remarks>
    private readonly Lazy<IMongoClient> _client;

    /// <summary>
    /// A database nobody else is using, for one test.
    /// </summary>
    /// <remarks>
    /// Per test, not per class: xUnit constructs the test class once per test case, so each one
    /// calls this and gets its own name. That is what makes a test asserting an empty collection
    /// safe to write.
    /// </remarks>
    public RadarDatabase Database(string name) =>
        new(_client.Value,
            $"radar-test-{name}-{Guid.NewGuid():N}",
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Off. Forty-three index creations against a database holding four documents
                    // and about to be dropped is pure cost, and the tests that need an index
                    // build it themselves rather than race the background task that would.
                    ["MongoDB:EnsureIndexes"] = "false",
                })
                .Build());

    public void Dispose() => _runner.Dispose();
}

/// <summary>
/// Shared by every test class that needs a database, so mongod starts once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RadarDatabaseCollection : ICollectionFixture<RadarDatabaseFixture>
{
    public const string Name = "mongo";
}

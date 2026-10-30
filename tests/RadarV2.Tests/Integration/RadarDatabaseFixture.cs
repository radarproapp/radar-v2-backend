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
    }

    /// <summary>A database nobody else is using, for one test class.</summary>
    public RadarDatabase Database(string name) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:ConnectionString"] = _runner.ConnectionString,
                ["MongoDB:DatabaseName"] = $"radar-test-{name}-{Guid.NewGuid():N}",
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

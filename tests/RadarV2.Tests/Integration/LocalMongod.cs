using EphemeralMongo;

namespace RadarV2.Tests.Integration;

/// <summary>
/// A real mongod for the test run, from wherever one can be had.
/// </summary>
/// <remarks>
/// EphemeralMongo downloads a MongoDB build and manages its lifecycle, which is the right default
/// and works unattended on a clean machine. It stopped working here, and the reason is worth
/// recording because the symptom was so misleading.
///
/// The Windows build of MongoDB 8.3 is an <b>881 MB</b> download. On a connection that sustains
/// roughly 600 KB/s and drops long transfers, it never finishes: it stalls part way, retries, and
/// leaves partial archives behind - we cleared a gigabyte of them. Every database-backed test
/// then failed in its constructor rather than at an assertion, including tests that touch no
/// database but share the fixture, so the suite looked broken in a dozen unrelated places.
///
/// Two attempted fixes failed and are recorded so nobody repeats them.
/// <list type="bullet">
/// <item><c>NewVersionCheckTimeout = TimeSpan.Zero</c> does not mean "skip the check". It appears
/// to mean no limit, and turned a slow failure into one that never returned.</item>
/// <item><c>BinaryDirectory</c>, pointed at a directory holding a working mongod, mongoimport and
/// mongoexport, is ignored: EphemeralMongo 3.0.0 downloads the archive anyway. Measured - the
/// temp directory filled with 160 MB of a new download while the option was set.</item>
/// </list>
///
/// So: if somebody tells us where a mongod already is, use it and manage nothing. Otherwise fall
/// back to EphemeralMongo exactly as before, which keeps a fresh clone and CI working untouched.
/// </remarks>
public static class LocalMongod
{
    /// <summary>
    /// A connection string for a mongod that is already running.
    /// </summary>
    /// <remarks>
    /// Set this and the suite neither downloads nor starts anything. Each test still gets its
    /// own database on that server, so isolation is unchanged - only the lifecycle moves out.
    /// </remarks>
    public const string ExistingServer = "RADAR_MONGO_URL";

    /// <summary>A running mongod, and whether this suite is responsible for stopping it.</summary>
    public interface IHost : IDisposable
    {
        string ConnectionString { get; }
    }

    public static IHost Start()
    {
        var existing = Environment.GetEnvironmentVariable(ExistingServer);

        return string.IsNullOrWhiteSpace(existing)
            ? new Downloaded()
            : new Borrowed(existing);
    }

    /// <summary>
    /// Somebody else's server, left running afterwards.
    /// </summary>
    /// <remarks>
    /// Deliberately does nothing on dispose. Stopping a server this suite did not start would
    /// break the next run, and whoever started it owns it.
    /// </remarks>
    private sealed class Borrowed(string connectionString) : IHost
    {
        public string ConnectionString { get; } = connectionString;

        public void Dispose() { }
    }

    /// <summary>The ordinary path: EphemeralMongo fetches and runs one.</summary>
    private sealed class Downloaded : IHost
    {
        private readonly IMongoRunner _runner =
            MongoRunner.Run(new MongoRunnerOptions { UseSingleNodeReplicaSet = false });

        public string ConnectionString => _runner.ConnectionString;

        public void Dispose() => _runner.Dispose();
    }
}

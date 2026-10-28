using System.Runtime.CompilerServices;
using RadarV2.Data;

namespace RadarV2.Tests;

/// <summary>
/// Registers the BSON conventions before any test touches a serializer.
/// </summary>
/// <remarks>
/// The driver caches a class map per type the first time it serializes or deserializes one, and
/// a convention registered afterwards does not apply to what is already cached. In the
/// application that is handled by RadarDatabase's static constructor, which runs before any
/// collection is reached.
///
/// Tests have no such ordering. Two tests added for the playback upsert and the article sort
/// render a definition with <c>BsonSerializer.SerializerRegistry.GetSerializer&lt;T&gt;()</c>,
/// which caches the map — so if either ran before BsonConventionTests, the convention was never
/// applied and that test failed. It passed locally and failed on CI, because xUnit does not
/// promise an order and the two machines picked different ones.
///
/// A module initializer runs once, before anything else in the assembly, which is the only
/// placement that does not depend on which test happens to go first.
/// </remarks>
internal static class TestConventions
{
    [ModuleInitializer]
    internal static void Register() => BsonConventions.Register();
}

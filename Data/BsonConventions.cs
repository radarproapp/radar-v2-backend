using MongoDB.Bson.Serialization.Conventions;

namespace RadarV2.Data;

/// <summary>
/// Serialization conventions applied to every Radar document type.
/// </summary>
/// <remarks>
/// The problem this fixes, found on staging: <c>/api/home</c> returned 500 for any profile with
/// interests, with
///
///   <c>FormatException: Element 'YourEdge' does not match any field or property of class
///   RadarV2.Models.ContentItem.</c>
///
/// <c>YourEdge</c> was a field on ContentItem that was removed. The documents written while it
/// existed still carry it, and the driver's default is to throw when it meets an element the
/// class cannot map. So a field deleted weeks ago made every *read* of those documents fail —
/// not the write, not a migration, the read — and it surfaced as a brief that would not load.
///
/// Not one attribute on ContentItem: none of the 36 persisted models had this, so each of them
/// is one removed field away from the same failure, and remembering an attribute at the moment
/// you delete a property is not a plan. The convention covers every current and future model.
///
/// What this gives up is real: a typo in a property name now silently reads as null instead of
/// throwing. That is the better trade for a schemaless store that has already outlived several
/// versions of its own models — a stale document should not be able to take down a page.
/// </remarks>
public static class BsonConventions
{
    private static bool _registered;

    /// <summary>
    /// Registers the conventions. Must run before the first document is serialized or
    /// deserialized, because the driver caches a class map per type on first use.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        ConventionRegistry.Register(
            "RadarV2",
            new ConventionPack { new IgnoreExtraElementsConvention(true) },
            type => type.Namespace?.StartsWith("RadarV2", StringComparison.Ordinal) == true);
    }
}

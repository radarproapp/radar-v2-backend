using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A note belongs to whoever wrote it.
/// </summary>
/// <remarks>
/// Reading, rewriting and deleting a note all took a note id and nothing else, so any signed-in
/// account could do all three to any note in the database. The ids are GUIDs, which makes them
/// hard to guess and is not an authorisation check: they travel in shared URLs, in logs, and in
/// the response to creating a note.
///
/// The owner is part of the query rather than a check made afterwards, because a check made
/// afterwards is one somebody can forget to make. Somebody else's note reads as a note that is
/// not there, which is both honest and the answer that says least.
/// </remarks>
public class NoteOwnershipTests
{
    private static string Render(FilterDefinition<Note> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter
            .Render(new RenderArgs<Note>(registry.GetSerializer<Note>(), registry))
            .ToString();
    }

    /// <summary>The query names the note and the owner, both.</summary>
    [Fact]
    public void TheQueryAsksWhoOwnsIt()
    {
        var rendered = Render(MongoNotebookService.OwnedBy("note-1", "user-1"));

        Assert.Contains("note-1", rendered);
        Assert.Contains("user-1", rendered);
        Assert.Contains("UserId", rendered);
    }

    /// <summary>Where the service being checked lives, found from wherever the tests are run.</summary>
    private static string ServiceSource(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Services", fileName));
    }

    /// <summary>
    /// And one filter does the read, the write and the delete.
    /// </summary>
    /// <remarks>
    /// Three separately written filters are three chances for one of them to be the old version,
    /// and the read being scoped while the delete is not is worse than neither being scoped:
    /// nothing looks wrong from the outside. Checked against the source because the suite has no
    /// database to check against, and an unscoped query here is worth catching in whatever way
    /// can actually catch it.
    /// </remarks>
    [Fact]
    public void NoNoteQueryAsksOnlyForTheId()
    {
        var source = ServiceSource("MongoNotebookService.cs");

        // The id alone, as a whole query predicate. The one inside OwnedBy is paired with the
        // owner, so it does not read like this.
        Assert.DoesNotContain("(n => n.Id == noteId)", source);

        // Every query that takes one note: the read, the read before a partial update, the write
        // and the delete.
        Assert.Equal(4, source.Split("OwnedBy(noteId, userId)").Length - 1);
    }

    /// <summary>
    /// A comparison is the same shape of thing: one record, one owner, reachable by id.
    /// </summary>
    /// <remarks>
    /// It carries whatever the reader asked to have compared, which is as much their own business
    /// as a note is.
    /// </remarks>
    [Fact]
    public void AComparisonIsScopedToItsOwnerToo()
    {
        var source = ServiceSource("MongoCompareService.cs");

        Assert.Contains("c.Id == comparisonId && c.UserId == userId", source);
    }
}

using RadarV2.Data;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// A note belongs to whoever wrote it, asked of the database.
/// </summary>
/// <remarks>
/// Reading, rewriting and deleting a note all took a note id and nothing else, so any signed-in
/// account could do all three to any note in the database.
///
/// The test written at the time asserted on the text of the service's own source file, because
/// there was nothing to query. That is a guard against the line being deleted, not a
/// demonstration that somebody else's note is out of reach - and the difference matters, because
/// the thing being claimed is a security property.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class NotebookOwnershipTests
{
    private readonly MongoNotebookService _notes;

    public NotebookOwnershipTests(RadarDatabaseFixture fixture) =>
        _notes = new MongoNotebookService(fixture.Database("notes"));

    private const string Owner = "the-owner";
    private const string Stranger = "somebody-else";

    [Fact]
    public async Task AStrangerCannotReadIt()
    {
        var note = await _notes.CreateNoteAsync(Owner, "Bank details", "The account number is 123");

        Assert.Null(await _notes.GetNoteByIdAsync(Stranger, note.Id));
        Assert.NotNull(await _notes.GetNoteByIdAsync(Owner, note.Id));
    }

    [Fact]
    public async Task AStrangerCannotRewriteIt()
    {
        var note = await _notes.CreateNoteAsync(Owner, "My plan", "The original words");

        var attempted = await _notes.UpdateNoteAsync(Stranger, note.Id, body: "Replaced by somebody else");

        Assert.Null(attempted);

        var unchanged = await _notes.GetNoteByIdAsync(Owner, note.Id);
        Assert.Equal("The original words", unchanged!.Body);
    }

    [Fact]
    public async Task AStrangerCannotDeleteIt()
    {
        var note = await _notes.CreateNoteAsync(Owner, "Keep this", "Please");

        await _notes.DeleteNoteAsync(Stranger, note.Id);

        Assert.NotNull(await _notes.GetNoteByIdAsync(Owner, note.Id));
    }

    /// <summary>And the owner can still do all three.</summary>
    [Fact]
    public async Task TheOwnerCanStillUseTheirOwnNote()
    {
        var note = await _notes.CreateNoteAsync(Owner, "Mine", "Original");

        var edited = await _notes.UpdateNoteAsync(Owner, note.Id, body: "Edited");
        Assert.Equal("Edited", edited!.Body);

        await _notes.DeleteNoteAsync(Owner, note.Id);
        Assert.Null(await _notes.GetNoteByIdAsync(Owner, note.Id));
    }

    /// <summary>A note nobody owns by that id is simply not there.</summary>
    [Fact]
    public async Task AnIdThatIsNotANoteIsNotFound()
    {
        Assert.Null(await _notes.GetNoteByIdAsync(Owner, "no-such-note"));
        Assert.Null(await _notes.UpdateNoteAsync(Owner, "no-such-note", body: "x"));
    }

    /// <summary>The list is only ever the asker's own.</summary>
    [Fact]
    public async Task TheListIsOnlyTheirOwn()
    {
        await _notes.CreateNoteAsync(Owner, "Mine", "a");
        await _notes.CreateNoteAsync(Stranger, "Theirs", "b");

        var mine = await _notes.GetNotesAsync(Owner);

        Assert.Equal("Mine", Assert.Single(mine).Title);
    }
}

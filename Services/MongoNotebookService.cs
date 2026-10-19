using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoNotebookService : INotebookService
{
    private readonly RadarDatabase _db;

    public MongoNotebookService(RadarDatabase db) => _db = db;

    /// <summary>
    /// Pinned notes first, then most recently edited. Archived notes are excluded unless asked for,
    /// which is what makes archiving worth doing instead of deleting.
    /// </summary>
    public async Task<List<Note>> GetNotesAsync(string userId, bool includeArchived = false)
    {
        var notes = await _db.Notes
            .Find(n => n.UserId == userId && (includeArchived || !n.IsArchived))
            .ToListAsync();

        return notes
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.EditedAt)
            .ToList();
    }

    public async Task<Note?> GetNoteByIdAsync(string noteId)
    {
        return await _db.Notes
            .Find(n => n.Id == noteId)
            .FirstOrDefaultAsync();
    }

    public async Task<Note> CreateNoteAsync(string userId, string title, string body)
    {
        var note = new Note
        {
            UserId = userId,
            Title = title,
            Body = body,
            CreatedAt = DateTime.UtcNow,
            EditedAt = DateTime.UtcNow,
        };
        await _db.Notes.InsertOneAsync(note);
        return note;
    }

    /// <summary>
    /// Partial update: anything left null is untouched. The grid edits one property at a time —
    /// ticking a checkbox, pinning, picking a colour — and a whole-object write would make each of
    /// those clobber whatever else had changed since the page loaded.
    /// </summary>
    public async Task<Note?> UpdateNoteAsync(
        string noteId,
        string? title = null,
        string? body = null,
        List<string>? tags = null,
        bool? isPinned = null,
        bool? isArchived = null,
        string? color = null,
        List<NoteChecklistItem>? checklist = null)
    {
        var note = await _db.Notes.Find(n => n.Id == noteId).FirstOrDefaultAsync();
        if (note is null) return null;

        if (title is not null) note.Title = title;
        if (body is not null) note.Body = body;
        if (tags is not null) note.Tags = tags;
        if (isPinned is not null) note.IsPinned = isPinned.Value;
        if (isArchived is not null) note.IsArchived = isArchived.Value;
        if (color is not null) note.Color = string.IsNullOrWhiteSpace(color) ? null : color;
        if (checklist is not null) note.Checklist = checklist;

        // Only a content change counts as an edit. Pinning or archiving should not reorder the grid
        // under the user's cursor.
        if (title is not null || body is not null || checklist is not null)
            note.EditedAt = DateTime.UtcNow;

        await _db.Notes.ReplaceOneAsync(n => n.Id == noteId, note);
        return note;
    }

    public async Task DeleteNoteAsync(string noteId)
    {
        await _db.Notes.DeleteOneAsync(n => n.Id == noteId);
    }
}

using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoNotebookService : INotebookService
{
    private readonly RadarDatabase _db;

    public MongoNotebookService(RadarDatabase db) => _db = db;

    public async Task<List<Note>> GetNotesAsync(string userId)
    {
        return await _db.Notes
            .Find(n => n.UserId == userId)
            .SortByDescending(n => n.EditedAt)
            .ToListAsync();
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

    public async Task UpdateNoteAsync(string noteId, string title, string body, List<string> tags)
    {
        var note = await _db.Notes.Find(n => n.Id == noteId).FirstOrDefaultAsync();
        if (note is null) return;

        note.Title = title;
        note.Body = body;
        note.Tags = tags;
        note.EditedAt = DateTime.UtcNow;

        await _db.Notes.ReplaceOneAsync(n => n.Id == noteId, note);
    }

    public async Task DeleteNoteAsync(string noteId)
    {
        await _db.Notes.DeleteOneAsync(n => n.Id == noteId);
    }
}

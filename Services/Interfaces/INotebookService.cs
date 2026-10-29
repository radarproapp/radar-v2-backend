using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface INotebookService
{
    Task<List<Note>> GetNotesAsync(string userId, bool includeArchived = false);
    /// <summary>One note, by id, belonging to the person asking.</summary>
    Task<Note?> GetNoteByIdAsync(string userId, string noteId);
    Task<Note> CreateNoteAsync(string userId, string title, string body);
    /// <summary>Partial update: anything left null is untouched.</summary>
    Task<Note?> UpdateNoteAsync(
        string userId,
        string noteId,
        string? title = null,
        string? body = null,
        List<string>? tags = null,
        bool? isPinned = null,
        bool? isArchived = null,
        string? color = null,
        List<NoteChecklistItem>? checklist = null);
    Task DeleteNoteAsync(string userId, string noteId);
}

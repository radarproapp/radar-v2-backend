using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface INotebookService
{
    Task<List<Note>> GetNotesAsync(string userId, bool includeArchived = false);
    Task<Note?> GetNoteByIdAsync(string noteId);
    Task<Note> CreateNoteAsync(string userId, string title, string body);
    /// <summary>Partial update: anything left null is untouched.</summary>
    Task<Note?> UpdateNoteAsync(
        string noteId,
        string? title = null,
        string? body = null,
        List<string>? tags = null,
        bool? isPinned = null,
        bool? isArchived = null,
        string? color = null,
        List<NoteChecklistItem>? checklist = null);
    Task DeleteNoteAsync(string noteId);
}

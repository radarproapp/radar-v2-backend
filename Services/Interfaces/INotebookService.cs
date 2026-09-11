using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface INotebookService
{
    Task<List<Note>> GetNotesAsync(string userId);
    Task<Note?> GetNoteByIdAsync(string noteId);
    Task<Note> CreateNoteAsync(string userId, string title, string body);
    Task UpdateNoteAsync(string noteId, string title, string body, List<string> tags);
    Task DeleteNoteAsync(string noteId);
}

using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ILibraryService
{
    Task<List<LibraryDocument>> GetAllAsync(string? category = null, int? year = null, string? search = null);
    Task<List<string>> GetCategoriesAsync();
    Task<LibraryDocument?> GetByIdAsync(string id);
}

using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoLibraryService : ILibraryService
{
    private readonly RadarDatabase _db;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoLibraryService(RadarDatabase db) => _db = db;

    public async Task<List<LibraryDocument>> GetAllAsync(string? category = null, int? year = null, string? search = null)
    {
        await EnsureSeededAsync();

        var builder = Builders<LibraryDocument>.Filter;
        var filter = builder.Empty;

        if (!string.IsNullOrWhiteSpace(category))
            filter &= builder.Eq(d => d.Category, category);

        if (year.HasValue)
            filter &= builder.Eq(d => d.Year, year.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var regex = new MongoDB.Bson.BsonRegularExpression(search, "i");
            filter &= builder.Or(
                builder.Regex(d => d.Title, regex),
                builder.Regex(d => d.Publisher, regex),
                builder.Regex(d => d.Subtopic, regex));
        }

        return await _db.LibraryDocuments
            .Find(filter)
            .SortByDescending(d => d.Year)
            .ThenBy(d => d.Title)
            .ToListAsync();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        await EnsureSeededAsync();
        var categories = await _db.LibraryDocuments
            .Distinct<string>("Category", Builders<LibraryDocument>.Filter.Empty)
            .ToListAsync();
        return categories.OrderBy(c => c).ToList();
    }

    public async Task<LibraryDocument?> GetByIdAsync(string id)
    {
        await EnsureSeededAsync();
        return await _db.LibraryDocuments.Find(d => d.Id == id).FirstOrDefaultAsync();
    }

    private async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            var count = await _db.LibraryDocuments.CountDocumentsAsync(Builders<LibraryDocument>.Filter.Empty);
            if (count == 0)
            {
                await _db.LibraryDocuments.InsertManyAsync(LibraryRegistry.All);
            }
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }
}

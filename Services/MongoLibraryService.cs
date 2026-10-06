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

            // Upsert by (Title, Category) rather than "insert only when empty": the curated registry
            // grows over time, and an existing database must pick up new documents (and edits) on
            // deploy without duplicating the ones already there.
            var existing = await _db.LibraryDocuments
                .Find(Builders<LibraryDocument>.Filter.Empty)
                .Project(d => new { d.Id, d.Title, d.Category })
                .ToListAsync();
            var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in existing)
                byKey[Key(doc.Title, doc.Category)] = doc.Id;

            var writes = new List<WriteModel<LibraryDocument>>();
            foreach (var document in LibraryRegistry.All)
            {
                var key = Key(document.Title, document.Category);
                if (byKey.TryGetValue(key, out var id))
                {
                    document.Id = id;
                    writes.Add(new ReplaceOneModel<LibraryDocument>(Builders<LibraryDocument>.Filter.Eq(d => d.Id, id), document));
                }
                else
                {
                    writes.Add(new InsertOneModel<LibraryDocument>(document));
                }
            }

            if (writes.Count > 0)
                await _db.LibraryDocuments.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false });

            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static string Key(string title, string category) => $"{title.Trim()}|{category.Trim()}";
}

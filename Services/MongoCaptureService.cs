using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Persists captured items (links, notes, voice transcriptions, photos) to MongoDB.
/// Each item is scoped to the user who created it.
/// </summary>
public class MongoCaptureService : ICaptureService
{
    private readonly RadarDatabase _db;

    public MongoCaptureService(RadarDatabase db) => _db = db;

    public async Task<CapturedItem> CaptureAsync(string userId, CaptureMode mode, string input)
    {
        var item = new CapturedItem
        {
            UserId = userId,
            Mode = mode,
            Input = input,
            Signal = input.Length > 60 ? input[..60] + "…" : input,
            Source = "captured just now",
            IsProcessing = false,
            CapturedAt = DateTime.UtcNow,
        };

        await _db.CapturedItems.InsertOneAsync(item);
        return item;
    }

    public async Task<List<CapturedItem>> GetRecentCapturesAsync(string userId)
    {
        return await _db.CapturedItems
            .Find(c => c.UserId == userId)
            .SortByDescending(c => c.CapturedAt)
            .Limit(50)
            .ToListAsync();
    }
}

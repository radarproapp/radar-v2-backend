using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Persists raw user/ranking events. Deliberately dumb — aggregation and quality analysis
/// (weekly best/worst review, "why helpful rate", ranking-weight tuning) happen out of band
/// against this collection, not here. See §3.1/§3.2 of the product-strategy notes.
/// </summary>
public class MongoAnalyticsService : IAnalyticsService
{
    private readonly RadarDatabase _db;

    public MongoAnalyticsService(RadarDatabase db) => _db = db;

    public Task LogEventAsync(AnalyticsEvent evt) => _db.AnalyticsEvents.InsertOneAsync(evt);

    public async Task<Dictionary<AnalyticsEventType, long>> GetEventCountsAsync(int trailingDays = 7)
    {
        var since = DateTime.UtcNow.AddDays(-trailingDays);
        var counts = new Dictionary<AnalyticsEventType, long>();

        foreach (var type in Enum.GetValues<AnalyticsEventType>())
        {
            counts[type] = await _db.AnalyticsEvents.CountDocumentsAsync(
                e => e.Type == type && e.CreatedAt >= since);
        }

        return counts;
    }
}

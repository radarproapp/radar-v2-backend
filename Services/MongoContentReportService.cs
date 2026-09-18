using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoContentReportService : IContentReportService
{
    private readonly RadarDatabase _db;

    public MongoContentReportService(RadarDatabase db) => _db = db;

    public Task CreateReportAsync(string userId, ContentItem item, ReportReason reason, string? note) =>
        _db.ContentReports.InsertOneAsync(new ContentReport
        {
            UserId = userId,
            ContentItemId = item.Id,
            ItemSignal = item.Signal,
            Source = item.Source,
            CredibilityTier = item.CredibilityTier,
            Reason = reason,
            Note = note,
        });

    public async Task<List<ContentReport>> GetOpenReportsAsync(int limit = 50) =>
        await _db.ContentReports
            .Find(r => !r.IsResolved)
            .SortByDescending(r => r.CreatedAt)
            .Limit(limit)
            .ToListAsync();

    public async Task<List<SourceReportSummary>> GetSourceSummaryAsync(int trailingDays = 30)
    {
        var since = DateTime.UtcNow.AddDays(-trailingDays);
        var reports = await _db.ContentReports
            .Find(r => r.CreatedAt >= since)
            .ToListAsync();

        return reports
            .GroupBy(r => r.Source)
            .Select(g => new SourceReportSummary
            {
                Source = g.Key,
                CredibilityTier = g.First().CredibilityTier,
                ReportCount = g.Count(),
                LastReportedAt = g.Max(r => r.CreatedAt),
            })
            .OrderByDescending(s => s.ReportCount)
            .ToList();
    }

    public Task ResolveReportAsync(string reportId) =>
        _db.ContentReports.UpdateOneAsync(
            r => r.Id == reportId,
            Builders<ContentReport>.Update.Set(r => r.IsResolved, true));
}

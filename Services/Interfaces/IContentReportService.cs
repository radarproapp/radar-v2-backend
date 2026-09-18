using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public class SourceReportSummary
{
    public string Source { get; set; } = string.Empty;
    public int CredibilityTier { get; set; }
    public int ReportCount { get; set; }
    public DateTime LastReportedAt { get; set; }
}

public interface IContentReportService
{
    Task CreateReportAsync(string userId, ContentItem item, ReportReason reason, string? note);
    Task<List<ContentReport>> GetOpenReportsAsync(int limit = 50);
    Task<List<SourceReportSummary>> GetSourceSummaryAsync(int trailingDays = 30);
    Task ResolveReportAsync(string reportId);
}

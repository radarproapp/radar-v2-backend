namespace RadarV2.Data.Documents;

public class IngestionQuotaDoc
{
    // Composite key: "{service}:{yyyy-MM}" e.g. "mediastack:2026-08"
    public string Id { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public decimal SpendUsd { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

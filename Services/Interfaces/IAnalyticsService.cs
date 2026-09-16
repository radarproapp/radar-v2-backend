using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IAnalyticsService
{
    Task LogEventAsync(AnalyticsEvent evt);

    /// <summary>Counts by event type over the trailing window, for a basic quality-metrics view (§6.3).</summary>
    Task<Dictionary<AnalyticsEventType, long>> GetEventCountsAsync(int trailingDays = 7);
}

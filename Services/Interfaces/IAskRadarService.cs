using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IAskRadarService
{
    Task<ChatMessage> SendMessageAsync(string userId, string message, List<ChatMessage> history);
    IAsyncEnumerable<string> StreamMessageAsync(string userId, string message, List<ChatMessage> history, CancellationToken ct = default);
    Task<string> SummarizeContentAsync(string contentItemId);
    Task<string> CreateStudyPlanAsync(string userId, string topic);
    Task<List<ContentItem>> RecommendResourcesAsync(string userId, string topic);
}

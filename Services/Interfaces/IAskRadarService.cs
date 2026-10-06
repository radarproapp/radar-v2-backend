using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IAskRadarService
{
    Task<ChatMessage> SendMessageAsync(string userId, string message, List<ChatMessage> history);
    IAsyncEnumerable<string> StreamMessageAsync(string userId, string message, List<ChatMessage> history, CancellationToken ct = default);
    Task<string> SummarizeContentAsync(string contentItemId);
    Task<string> CreateStudyPlanAsync(string userId, string topic);
    Task<List<ContentItem>> RecommendResourcesAsync(string userId, string topic);

    /// <summary>The user's saved Ask Radar conversation, oldest first.</summary>
    Task<List<ChatMessage>> GetHistoryAsync(string userId, int limit = 60);

    /// <summary>Appends messages to the user's saved conversation (trimmed to a rolling window).</summary>
    Task AppendAsync(string userId, IReadOnlyList<ChatMessage> messages);

    /// <summary>Starts a fresh conversation by removing the user's saved history.</summary>
    Task ClearHistoryAsync(string userId);
}

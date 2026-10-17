using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

/// <summary>
/// Single boundary for all model calls. Features must not create their own OpenRouter clients.
/// </summary>
public interface IAiEngine
{
    Task<string?> GenerateTextAsync(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<T?> GenerateJsonAsync<T>(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(string feature, string systemPrompt, IReadOnlyCollection<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>Embedding vector for semantic search/recommendations. Null when unavailable.</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}

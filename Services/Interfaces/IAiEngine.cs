using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

/// <summary>
/// Central processor and single boundary for all Radar intelligence activities.
/// All features (feed personalization, learning paths, learn hub, clips, opportunities,
/// academic synthesis, mentor tutoring, and roadmaps) process through this engine.
/// </summary>
public interface IAiEngine
{
    Task<string?> GenerateTextAsync(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<T?> GenerateJsonAsync<T>(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(string feature, string systemPrompt, IReadOnlyCollection<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>Embedding vector for semantic search/recommendations. Null when unavailable.</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Records an audited activity processed through the central AI engine.</summary>
    Task RecordActivityAsync(string feature, string? userId, string mode, bool success, long durationMs, string? summary = null, CancellationToken ct = default);

    /// <summary>Diagnostic status and activity telemetry across all Radar subsystems.</summary>
    Task<AiEngineStats> GetEngineStatsAsync(CancellationToken ct = default);
}

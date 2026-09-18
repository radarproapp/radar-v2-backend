using System.Text.Json;

namespace RadarV2.Helpers;

/// <summary>
/// Writes an IAsyncEnumerable&lt;string&gt; chat stream out as SSE — shared by Ask Radar and
/// Mentor chat so both controllers stream identically (REACT_MIGRATION.md §5 Phase 3 step 5).
/// </summary>
public static class SseWriter
{
    public static async Task WriteChatStreamAsync(HttpResponse response, IAsyncEnumerable<string> chunks, CancellationToken ct)
    {
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in chunks.WithCancellation(ct))
            {
                var payload = JsonSerializer.Serialize(new { content = chunk });
                await response.WriteAsync($"data: {payload}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected mid-stream — nothing to clean up
            return;
        }

        await response.WriteAsync("data: [DONE]\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

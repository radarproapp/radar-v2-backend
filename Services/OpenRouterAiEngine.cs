using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public sealed class OpenRouterAiEngine : IAiEngine
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly RadarDatabase _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenRouterAiEngine> _log;

    public OpenRouterAiEngine(IHttpClientFactory httpFactory, IConfiguration config, RadarDatabase db, IMemoryCache cache, ILogger<OpenRouterAiEngine> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _cache = cache;
        _log = log;
    }

    public async Task<T?> GenerateJsonAsync<T>(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var cacheKey = $"radar:ai:{feature}:{Hash(systemPrompt + "\n" + userPrompt)}";
        if (_cache.TryGetValue(cacheKey, out T? cached)) return cached;
        if (!await WithinBudgetAsync(feature, ct)) return default;

        var body = JsonSerializer.Serialize(new
        {
            model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
            messages = new[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userPrompt } },
            response_format = new { type = "json_object" },
            temperature = .25,
        });

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                using var response = await _httpFactory.CreateClient("OpenRouter").SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _log.LogWarning("OpenRouter feature {Feature} returned {Status} on attempt {Attempt}", feature, response.StatusCode, attempt);
                    if (attempt == 2) return default;
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var envelope = JsonDocument.Parse(json);
                var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content)) return default;
                var value = JsonSerializer.Deserialize<T>(StripFence(content), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (value is not null)
                {
                    _cache.Set(cacheKey, value, TimeSpan.FromHours(_config.GetValue("OpenRouter:CacheHours", 12)));
                    await RecordUsageAsync(feature, envelope.RootElement, ct);
                }
                return value;
            }
            catch (Exception ex) when (attempt < 2 && ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "OpenRouter feature {Feature} failed; retrying", feature);
                await Task.Delay(250, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OpenRouter feature {Feature} failed", feature);
                return default;
            }
        }
        return default;
    }

    public async Task<string?> GenerateTextAsync(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!await WithinBudgetAsync(feature, ct)) return null;
        var body = JsonSerializer.Serialize(new
        {
            model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
            messages = new[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userPrompt } },
            temperature = .25,
        });
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            using var response = await _httpFactory.CreateClient("OpenRouter").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(ct);
            using var envelope = JsonDocument.Parse(json);
            var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            await RecordUsageAsync(feature, envelope.RootElement, ct);
            return content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "OpenRouter text feature {Feature} failed", feature);
            return null;
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(string feature, string systemPrompt, IReadOnlyCollection<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!await WithinBudgetAsync(feature, ct))
        {
            yield return "Radar has reached its AI usage limit for now. Please try again later.";
            yield break;
        }

        var promptMessages = new List<object> { new { role = "system", content = systemPrompt } };
        promptMessages.AddRange(messages.TakeLast(20).Select(m => new { role = m.Role, content = m.Content }));
        var body = JsonSerializer.Serialize(new { model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat", messages = promptMessages, stream = true });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        HttpResponseMessage? response = null;
        var connectionFailed = false;
        try
        {
            response = await _httpFactory.CreateClient("OpenRouter").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "OpenRouter streaming feature {Feature} failed", feature);
            connectionFailed = true;
        }

        if (connectionFailed || response is null)
        {
            yield return "I cannot reach Radar's AI engine right now. Please try again.";
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = line[6..];
            if (data == "[DONE]") break;
            string? chunk = null;
            try { using var doc = JsonDocument.Parse(data); chunk = doc.RootElement.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString(); } catch (JsonException) { }
            if (!string.IsNullOrEmpty(chunk)) yield return chunk;
        }
    }

    private async Task<bool> WithinBudgetAsync(string feature, CancellationToken ct)
    {
        var cap = _config.GetValue<decimal>("OpenRouter:MonthlySpendCapUsd", 25m);
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var quota = await _db.IngestionQuotas.Find(q => q.Id == $"openrouter:{month}").FirstOrDefaultAsync(ct);
        return quota is null || quota.SpendUsd < cap;
    }

    private async Task RecordUsageAsync(string feature, JsonElement envelope, CancellationToken ct)
    {
        if (!envelope.TryGetProperty("usage", out var usage)) return;
        var input = usage.TryGetProperty("prompt_tokens", out var i) ? i.GetInt64() : 0;
        var output = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt64() : 0;
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var spend = input / 1_000_000m * .27m + output / 1_000_000m * 1.10m;
        await _db.IngestionQuotas.UpdateOneAsync(q => q.Id == $"openrouter:{month}", Builders<IngestionQuotaDoc>.Update.SetOnInsert(q => q.Id, $"openrouter:{month}").SetOnInsert(q => q.Service, "openrouter").SetOnInsert(q => q.Month, month).Inc(q => q.RequestCount, 1).Inc(q => q.TotalInputTokens, input).Inc(q => q.TotalOutputTokens, output).Inc(q => q.SpendUsd, spend).Set(q => q.UpdatedAt, DateTime.UtcNow), new UpdateOptions { IsUpsert = true }, ct);
    }

    private static string StripFence(string value)
    {
        var result = value.Trim();
        if (!result.StartsWith("```")) return result;
        result = result[(result.IndexOf('\n') + 1)..];
        return result.EndsWith("```") ? result[..result.LastIndexOf("```", StringComparison.Ordinal)].Trim() : result.Trim();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}

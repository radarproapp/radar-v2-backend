using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Generates and caches a per-user "why this matters" sentence, distinct from the global
/// ContentItem.WhyItMatters generated once at ingestion. This is the mechanism behind the
/// product-strategy §3.3 "why" gate — see IPersonalizedWhyService for the contract.
///
/// Deliberately NOT wired into the feed list as a hard filter yet: doing so would hide every
/// item whenever OpenRouter:ApiKey is empty (the common case today per HANDOFF.md), which would
/// silently empty the feed. That flip is a product decision, not something to default to here.
/// </summary>
public class PersonalizedWhyService : IPersonalizedWhyService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly RadarDatabase _db;
    private readonly ILogger<PersonalizedWhyService> _log;

    public PersonalizedWhyService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        ILogger<PersonalizedWhyService> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _log = log;
    }

    public async Task<UserContentWhy?> GetOrGenerateAsync(UserProfile profile, ContentItem item, CancellationToken ct = default)
    {
        var personaSnapshot = profile.Persona.ToString();
        var existing = await _db.UserContentWhys
            .Find(w => w.UserId == profile.Id && w.ContentItemId == item.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is not null
            && existing.IsGenerated
            && existing.GoalSnapshot == profile.PrimaryGoal
            && existing.PersonaSnapshot == personaSnapshot)
        {
            return existing;
        }

        var generated = await GenerateAsync(profile, item, ct);
        if (generated is null) return existing; // stale cache beats nothing if regeneration fails

        var doc = new UserContentWhy
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            UserId = profile.Id,
            ContentItemId = item.Id,
            WhyText = generated,
            GoalSnapshot = profile.PrimaryGoal,
            PersonaSnapshot = personaSnapshot,
            GeneratedAt = DateTime.UtcNow,
            IsGenerated = true,
            IsHelpful = existing?.IsHelpful,
            RatedAt = existing?.RatedAt,
        };

        await _db.UserContentWhys.ReplaceOneAsync(
            w => w.UserId == profile.Id && w.ContentItemId == item.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return doc;
    }

    public async Task RateAsync(string userId, string contentItemId, string whyTextShown, bool helpful)
    {
        // Raw UpdateOneAsync+SetOnInsert would leave Mongo to generate _id itself on insert,
        // which doesn't round-trip cleanly against this class's string-typed Id — so read/write
        // a full POCO instead, same as every other upsert in this codebase.
        var existing = await _db.UserContentWhys
            .Find(w => w.UserId == userId && w.ContentItemId == contentItemId)
            .FirstOrDefaultAsync();

        var doc = existing ?? new UserContentWhy
        {
            UserId = userId,
            ContentItemId = contentItemId,
            WhyText = whyTextShown,
        };
        doc.IsHelpful = helpful;
        doc.RatedAt = DateTime.UtcNow;

        await _db.UserContentWhys.ReplaceOneAsync(
            w => w.UserId == userId && w.ContentItemId == contentItemId,
            doc,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task<(long Helpful, long NotHelpful)> GetHelpfulRateAsync(int trailingDays = 30)
    {
        var since = DateTime.UtcNow.AddDays(-trailingDays);
        var helpful = await _db.UserContentWhys.CountDocumentsAsync(
            w => w.IsHelpful == true && w.RatedAt >= since);
        var notHelpful = await _db.UserContentWhys.CountDocumentsAsync(
            w => w.IsHelpful == false && w.RatedAt >= since);
        return (helpful, notHelpful);
    }

    // ── Generation ───────────────────────────────────────────────────────────

    private async Task<string?> GenerateAsync(UserProfile profile, ContentItem item, CancellationToken ct)
    {
        var apiKey = _config["OpenRouter:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogDebug("OpenRouter API key not set — skipping personalized why for user {UserId}", profile.Id);
            return null;
        }

        if (!await IsWithinSpendCapAsync(ct))
        {
            _log.LogWarning("Personalized-why spend cap reached — skipping for user {UserId}", profile.Id);
            return null;
        }

        try
        {
            var prompt = BuildPrompt(profile, item);
            var body = JsonSerializer.Serialize(new
            {
                model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
                messages = new[] { new { role = "user", content = prompt } },
                response_format = new { type = "json_object" },
                temperature = 0.4,
            });

            var client = _httpFactory.CreateClient("OpenRouter");
            var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            if (doc.RootElement.TryGetProperty("usage", out var u))
            {
                var input = u.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt64() : 0L;
                var output = u.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt64() : 0L;
                await RecordUsageAsync(input, output, ct);
            }

            if (string.IsNullOrWhiteSpace(content)) return null;

            content = content.Trim();
            if (content.StartsWith("```")) content = content.Split('\n', 2)[1];
            if (content.EndsWith("```")) content = content[..content.LastIndexOf("```")];

            using var result = JsonDocument.Parse(content.Trim());
            var why = result.RootElement.TryGetProperty("why", out var w) ? w.GetString() : null;
            return string.IsNullOrWhiteSpace(why) ? null : why.Trim();
        }
        catch (Exception ex)
        {
            _log.LogWarning("Personalized-why generation failed for user {UserId}: {Message}", profile.Id, ex.Message);
            return null;
        }
    }

    private static string BuildPrompt(UserProfile profile, ContentItem item) => $"""
        You write ONE short sentence (max 220 characters) explaining why a piece of content
        matters to a specific person. Reference their actual situation — do not use filler
        phrases like "this is important" or "you should know this".

        Person: {profile.Persona} based in {profile.City}, {profile.Region}. Their stated goal:
        "{profile.PrimaryGoal}".

        Content: "{item.Title}"
        What happened: {item.WhatHappened}

        Return strict JSON with exactly one field named "why" holding your sentence as a string.
        """;

    // ── Spend cap (separate small budget from ingestion enrichment) ────────────

    private async Task<bool> IsWithinSpendCapAsync(CancellationToken ct)
    {
        var cap = _config.GetValue<decimal>("OpenRouter:WhyMonthlySpendCapUsd", 10m);
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var filter = Builders<IngestionQuotaDoc>.Filter.Eq(q => q.Id, $"openrouter-why:{month}");
        var doc = await _db.IngestionQuotas.Find(filter).FirstOrDefaultAsync(ct);
        return doc is null || doc.SpendUsd < cap;
    }

    private async Task RecordUsageAsync(long inputTokens, long outputTokens, CancellationToken ct)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var id = $"openrouter-why:{month}";
        var spend = (inputTokens / 1_000_000m * 0.27m) + (outputTokens / 1_000_000m * 1.10m);
        var filter = Builders<IngestionQuotaDoc>.Filter.Eq(q => q.Id, id);
        var update = Builders<IngestionQuotaDoc>.Update
            .SetOnInsert(q => q.Service, "openrouter-why")
            .SetOnInsert(q => q.Month, month)
            .Inc(q => q.RequestCount, 1)
            .Inc(q => q.TotalInputTokens, inputTokens)
            .Inc(q => q.TotalOutputTokens, outputTokens)
            .Inc(q => q.SpendUsd, spend)
            .Set(q => q.UpdatedAt, DateTime.UtcNow);
        await _db.IngestionQuotas.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
    }
}

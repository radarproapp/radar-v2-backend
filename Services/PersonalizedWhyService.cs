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
    private readonly IAiEngine _ai;

    public PersonalizedWhyService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        ILogger<PersonalizedWhyService> log,
        IAiEngine ai)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _log = log;
        _ai = ai;
    }

    public async Task<UserContentWhy?> GetOrGenerateAsync(UserProfile profile, ContentItem item, CancellationToken ct = default)
    {
        var personaSnapshot = profile.Persona.ToString();
        var interestContextSnapshot = string.Join("|", InterestPersonalization.Contexts(profile).Select(c => $"{c.Interest}:{c.Goal}:{c.Level}:{c.Lens}"));
        var existing = await _db.UserContentWhys
            .Find(w => w.UserId == profile.Id && w.ContentItemId == item.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is not null
            && existing.IsGenerated
            && existing.GoalSnapshot == profile.PrimaryGoal
            && existing.PersonaSnapshot == personaSnapshot
            && existing.InterestContextSnapshot == interestContextSnapshot)
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
            InterestContextSnapshot = interestContextSnapshot,
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
        var result = await _ai.GenerateJsonAsync<WhyResponse>(
            "personalized-why",
            "You explain why content matters to one person. Return strict JSON with one string field named why.",
            BuildPrompt(profile, item),
            ct);
        return string.IsNullOrWhiteSpace(result?.Why) ? null : result.Why.Trim();
    }

    private sealed class WhyResponse { public string Why { get; set; } = string.Empty; }

    private static string BuildPrompt(UserProfile profile, ContentItem item) => $"""
        You write ONE short sentence (max 220 characters) explaining why a piece of content
        matters to a specific person. Reference their actual situation — do not use filler
        phrases like "this is important" or "you should know this". Mention the user's goal,
        active path, level, skills or industry when genuinely relevant. Never describe why it
        matters to a generic audience.

        Person: {profile.Persona} based in {profile.City}, {profile.Region}. Their stated goal:
        "{profile.PrimaryGoal}".
        Their active interest path is {InterestPathService.Build(profile).FirstOrDefault(p => p.IsPrimary)?.Title ?? profile.PrimaryGoal}, with context:
        {string.Join("; ", InterestPersonalization.Contexts(profile).Select(c => $"{c.Interest} ({c.Level}; focus: {c.Lens})"))}.
        Their profile details are: {string.Join(", ", profile.PersonaDetails.Select(pair => $"{pair.Key}: {pair.Value}"))}.
        Their current learning activity includes {profile.Stats.LessonsCompleted} completed lessons, {profile.Stats.ArticlesRead} articles and {profile.Stats.ProjectsCompleted} projects.

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

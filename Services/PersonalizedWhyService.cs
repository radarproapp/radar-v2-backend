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
            && !string.IsNullOrWhiteSpace(existing.WhyText)
            && !string.IsNullOrWhiteSpace(existing.YourEdge)
            && !string.IsNullOrWhiteSpace(existing.NextMove)
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
            WhyText = generated.Why,
            YourEdge = generated.YourEdge,
            NextMove = generated.NextMove,
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

    private async Task<WhyResponse?> GenerateAsync(UserProfile profile, ContentItem item, CancellationToken ct)
    {
        var result = await _ai.GenerateJsonAsync<WhyResponse>(
            "personalized-why",
            SystemPrompt,
            BuildPrompt(profile, item),
            ct);

        if (result is null || string.IsNullOrWhiteSpace(result.Why)) return null;

        return new WhyResponse
        {
            Why = result.Why.Trim(),
            YourEdge = result.YourEdge?.Trim() ?? string.Empty,
            NextMove = result.NextMove?.Trim() ?? string.Empty,
        };
    }

    private sealed class WhyResponse
    {
        public string Why { get; set; } = string.Empty;
        public string YourEdge { get; set; } = string.Empty;
        public string NextMove { get; set; } = string.Empty;
    }

    private const string SystemPrompt = """
        You are Radar's Personalized Intelligence Engine. You transform market developments and content items into high-utility, bespoke intelligence for a specific professional or founder.

        The Golden Rule:
        Radar must never simply tell the user: "Here is some information."
        It must tell them: "Here is what happened -> here is why it matters to YOU -> here is your edge/advantage -> here is what you can do about it."
        The goal is to transform information into personalized intelligence and action.

        You must answer three core questions in strict JSON format:
        {
          "why": "...",
          "yourEdge": "...",
          "nextMove": "..."
        }

        1. "why" (WHY IT MATTERS):
        Explain why this information matters specifically to this user.
        Connect the information to the user's:
        - Stated goals and target role
        - Interests and focus lenses
        - Current skills and capabilities
        - Problems and challenges they are tackling
        - Geographic location (e.g., Lagos, Nairobi, Accra, London) and target industry
        - Current learning roadmap and progress
        Do not give generic statements such as: "This is important because AI is transforming the world."
        Reason across intersections (e.g., user interested in AI + finance + data in Nigeria -> explain how financial institutions are adopting AI for credit/fraud and what gaps exist). Include concrete examples (e.g., 'A skill that becomes more valuable is...', 'This connects to something you are already learning...', 'A company that could be affected is...').
        Must be at least 45-60 words.

        2. "yourEdge" (YOUR EDGE):
        Answer: "What can you understand, learn, build or do because you know this information?"
        Identify a specific advantage available to the user.
        Connect the information to relevant:
        - Skills to develop
        - Knowledge gaps to close
        - Specific projects or artifacts to build
        - People or organizations to watch
        - Markets to explore
        - Career opportunities or business ideas
        Provide relevant examples. The Edge should make the user think: "I can actually do something with this."
        Must be at least 45-60 words.

        3. "nextMove" (NEXT MOVE):
        Give the user specific actions, not general advice.
        Instead of: "Learn more about this."
        Provide 4 to 5 numbered steps (1. ..., 2. ..., 3. ..., 4. ..., 5. ...) using direct action verbs:
        - Read the official policy / paper / analysis
        - Identify 2-3 specific organizations affected
        - Spend 30 minutes researching their response
        - Build a specific project / artifact / prototype
        - Add this skill or topic to your roadmap and revisit in seven days
        Must be formatted as a numbered list with steps 1 to 5.

        Standard:
        The combined Why It Matters + Your Edge + Next Move must be at least 100 words (typically 130-190 words). Every sentence must provide useful information. Return valid JSON only.
        """;

    private static string BuildPrompt(UserProfile profile, ContentItem item) => $"""
        User Profile:
        - Persona: {profile.Persona}
        - Location: {profile.City}, {profile.Region} (Geography: {string.Join(", ", profile.Geography)})
        - Primary Goal: "{profile.PrimaryGoal}"
        - Target Role: {profile.TargetRole}
        - Target Industry: {profile.TargetIndustry}
        - Current Intent: {profile.CurrentIntent}
        - Capabilities / Skills: {string.Join(", ", profile.Capabilities)}
        - Challenges / Problems: {string.Join(", ", profile.Problems)}
        - Active Interest Path: {InterestPathService.Build(profile).FirstOrDefault(p => p.IsPrimary)?.Title ?? profile.PrimaryGoal}
        - Interest Contexts: {string.Join("; ", InterestPersonalization.Contexts(profile).Select(c => $"{c.Interest} (Level: {c.Level}; Focus: {c.Lens}; Goal: {c.Goal})"))}
        - Learning Activity: {profile.Stats.LessonsCompleted} lessons completed, {profile.Stats.ArticlesRead} articles read, {profile.Stats.ProjectsCompleted} projects completed.

        Content Item:
        - Title: "{item.Title}"
        - Signal: "{item.Signal}"
        - Topic: "{item.Topic}" (Secondary: {string.Join(", ", item.SecondaryTopics)})
        - Source: "{item.Source}" (Credibility Tier: {item.CredibilityTier}, Layer: {item.Layer})
        - What Happened: {item.WhatHappened}
        - Key Insights: {string.Join("; ", item.KeyInsights)}
        - Summary: {item.AiSummary}

        Generate the bespoke, high-utility personalized intelligence answering "why", "yourEdge", and "nextMove" as strict JSON.
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

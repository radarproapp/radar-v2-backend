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
    private readonly RadarDatabase _db;
    private readonly ILogger<PersonalizedWhyService> _log;
    private readonly IAiEngine _ai;
    private readonly IUserLearningRecordService _records;

    public PersonalizedWhyService(
        RadarDatabase db,
        ILogger<PersonalizedWhyService> log,
        IAiEngine ai,
        IUserLearningRecordService records)
    {
        _db = db;
        _log = log;
        _ai = ai;
        _records = records;
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
            && !string.IsNullOrWhiteSpace(existing.NextMove)
            && existing.GoalSnapshot == profile.PrimaryGoal
            && existing.PersonaSnapshot == personaSnapshot
            && existing.InterestContextSnapshot == interestContextSnapshot)
        {
            return existing;
        }

        var record = await _records.BuildAsync(profile, ct);
        var generated = await GenerateAsync(profile, item, record, ct);
        if (generated is null) return existing; // stale cache beats nothing if regeneration fails

        var doc = new UserContentWhy
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            UserId = profile.Id,
            ContentItemId = item.Id,
            WhyText = generated.Why,
            CanConnect = generated.CanConnect,
            Relevance = generated.Relevance,
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

    private async Task<WhyResponse?> GenerateAsync(
        UserProfile profile, ContentItem item, UserLearningRecord? record, CancellationToken ct)
    {
        var result = await _ai.GenerateJsonAsync<WhyResponse>(
            "personalized-why",
            SystemPrompt,
            BuildPrompt(profile, item, record),
            ct);

        if (result is null) return null;

        // The writer was asked to decline rather than manufacture a link. Taking that seriously is
        // the whole point: returning null here lets the caller fall back to the grounded template,
        // instead of showing a stretched explanation that teaches the reader to distrust Radar.
        if (!result.CanConnect || string.IsNullOrWhiteSpace(result.Why))
        {
            _log.LogInformation(
                "Personalised why declined for item {ItemId} and user {UserId} (relevance {Relevance:0.00}) - no honest connection.",
                item.Id, profile.Id, result.Relevance);
            return null;
        }

        return new WhyResponse
        {
            Why = result.Why.Trim(),
            NextMove = result.NextMove?.Trim() ?? string.Empty,
            CanConnect = true,
            Relevance = result.Relevance <= 0 ? 1.0 : Math.Clamp(result.Relevance, 0, 1),
        };
    }

    private sealed class WhyResponse
    {
        public string Why { get; set; } = string.Empty;
        public string YourEdge { get; set; } = string.Empty;
        public string NextMove { get; set; } = string.Empty;
        public bool CanConnect { get; set; } = true;
        public double Relevance { get; set; } = 1.0;
    }

    private const string SystemPrompt = """
        You are Radar's personal intelligence writer. You take one piece of content and one real
        person, and you explain what it means FOR THEM.

        Return strict JSON only:
        {
          "canConnect": true,
          "relevance": 0.0,
          "why": "...",
          "nextMove": "..."
        }

        STEP 1 - BE HONEST FIRST.
        Before writing anything, ask: can I draw a real line from this content to this person's
        goal, role, skills, problems, roadmap or market?
        - If yes: set "canConnect" to true and "relevance" between 0.5 and 1.0.
        - If no: set "canConnect" to false, "relevance" below 0.4, leave "why" and "nextMove" empty,
          and stop. Do not invent a link.

        This matters. A SOC analyst was once told that a podcast about logistics and agriculture
        "can align with your cybersecurity skills". That is the failure you are avoiding. A stretched
        connection is worse than no connection, because it teaches the reader to stop trusting you.

        STEP 2 - "why": WHY IT MATTERS TO THIS PERSON.
        Tie the content to specifics from their profile: their goal, target role, industry, the
        skills they have, the problem they said is blocking them, their city and market, and where
        they currently are on their learning roadmap.
        - Name the thing. "This affects SOC analysts because..." not "this is important for
          professionals".
        - Reason across the overlap. If they care about AI and finance in Lagos, talk about what
          Nigerian banks are doing with AI for fraud and credit, and what gap that leaves.
        - Give at least one concrete example: a company this affects, a skill that just became more
          valuable, a market worth watching, or something they are already learning that connects.
        - Never write a sentence that would be true for any reader. If you could paste it under a
          different article, it is wrong.
        - About 60 to 90 words.

        STEP 3 - "nextMove": WHAT THEY SHOULD DO.
        Three to five numbered steps, each one something they could start today.
        - Each step names a thing: a document to read, an organisation to look up, a skill to add to
          the roadmap, something specific to build.
        - No "learn more about this" and no "stay informed".
        - Point at least one step at something they can show afterwards: a note, a rule, a small
          project, a write-up.
        - Put each step on its own line, numbered 1., 2., 3.

        LENGTH.
        "why" plus "nextMove" should normally come to at least 100 words. But never pad. If you have
        said the useful thing in fewer words, stop. A short, sharp answer beats a long, vague one.
        """;

    /// <summary>
    /// Everything the writer needs about this person. Includes the learning record, because spec
    /// item 12 asks the explanation to connect to the user's current roadmap — and without it the
    /// model could only ever talk about declared interests, never about where they actually are.
    /// </summary>
    private static string BuildPrompt(UserProfile profile, ContentItem item, UserLearningRecord? record) => $"""
        THE PERSON
        - Goal: "{profile.PrimaryGoal}"
        - Target role: {Or(profile.TargetRole, "not stated")}
        - Industry: {Or(profile.TargetIndustry, "not stated")}
        - Location: {Or(profile.City, profile.Region)}
        - Skills they have: {Or(string.Join(", ", profile.Capabilities), "none stated")}
        - What is blocking them: {Or(string.Join(", ", profile.Problems), "nothing stated")}
        - Interests: {Or(string.Join(", ", profile.Interests), "none stated")}
        - Working on right now: {Or(profile.CurrentIntent, "not stated")}

        WHERE THEY ARE IN THEIR LEARNING
        - Stage: {Or(record?.RecommendedStartStage, "just starting")}
        - Already proved: {Or(string.Join(", ", record?.MasteredTopics ?? []), "nothing yet")}
        - Struggling with: {Or(string.Join(", ", record?.WeakAreas ?? []), "nothing recorded")}
        - Keeps coming back to: {Or(string.Join(", ", (record?.Signals ?? []).Where(x => x.Source == "Inferred").Take(4).Select(x => x.Term)), "no pattern yet")}
        - Done so far: {record?.LessonsCompleted ?? 0} lessons, {record?.QuizzesTaken ?? 0} quizzes

        THE CONTENT
        - Headline: "{Or(item.Signal, item.Title)}"
        - Title: "{item.Title}"
        - Topic: {Or(item.Topic, "unclassified")}{(item.SecondaryTopics.Count > 0 ? $" (also: {string.Join(", ", item.SecondaryTopics)})" : "")}
        - Source: {item.Source}
        - What happened: {Or(item.WhatHappened, item.AiSummary)}
        - Key points: {Or(string.Join("; ", item.KeyInsights), "none extracted")}

        Decide honestly whether this content connects to this person, then write the JSON.
        """;

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

}

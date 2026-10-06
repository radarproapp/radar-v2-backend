using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class OpenRouterAskRadarService : IAskRadarService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly RadarDatabase _db;
    private readonly IUserSessionService _session;
    private readonly IAiEngine _ai;

    public OpenRouterAskRadarService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        IUserSessionService session,
        IAiEngine ai)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _session = session;
        _ai = ai;
    }

    public async Task<ChatMessage> SendMessageAsync(string userId, string message, List<ChatMessage> history)
    {
        var fullResponse = new StringBuilder();
        await foreach (var chunk in StreamMessageAsync(userId, message, history))
            fullResponse.Append(chunk);

        return new ChatMessage { Role = "assistant", Content = RadarV2.Helpers.PlainText.Clean(fullResponse.ToString()) };
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string userId,
        string message,
        List<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(userId);
        var messages = history.TakeLast(20).ToList();
        messages.Add(new ChatMessage { Role = "user", Content = message });
        await foreach (var chunk in _ai.StreamChatAsync("ask-radar", BuildSystemPrompt(profile), messages, ct))
            yield return chunk;
    }

    public async Task<string> SummarizeContentAsync(string contentItemId)
    {
        var item = await _db.ContentItems.Find(i => i.Id == contentItemId).FirstOrDefaultAsync();
        if (item == null) return "Content not found.";

        var prompt = $"Summarise this in 3 concise bullet points for a learner:\n\nTitle: {item.Title}\n\n{item.WhatHappened}\n\nKey insights: {string.Join("; ", item.KeyInsights)}";
        var response = new StringBuilder();
        await foreach (var chunk in StreamMessageAsync("", prompt, []))
            response.Append(chunk);

        return response.ToString();
    }

    public async Task<string> CreateStudyPlanAsync(string userId, string topic)
    {
        var profile = await GetProfileAsync(userId);
        var prompt = $"Create a focused 4-week study plan for {topic} for a {profile?.Persona.ToString() ?? "learner"} whose goal is: {profile?.PrimaryGoal ?? "professional growth"}. Be specific and actionable. Use markdown.";
        var response = new StringBuilder();
        await foreach (var chunk in StreamMessageAsync(userId, prompt, []))
            response.Append(chunk);
        return response.ToString();
    }

    public Task<List<ContentItem>> RecommendResourcesAsync(string userId, string topic) =>
        Task.FromResult(new List<ContentItem>());

    private async Task<UserProfile?> GetProfileAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null) return null;
        return await _db.Profiles.Find(p => p.Id == user.ProfileId).FirstOrDefaultAsync();
    }

    public async Task<List<ChatMessage>> GetHistoryAsync(string userId, int limit = 60)
    {
        if (string.IsNullOrEmpty(userId)) return [];
        var session = await _db.ChatSessions.Find(s => s.UserId == userId).FirstOrDefaultAsync();
        if (session?.Messages is null || session.Messages.Count == 0) return [];
        return session.Messages.Count <= limit ? session.Messages : session.Messages[^limit..];
    }

    public async Task AppendAsync(string userId, IReadOnlyList<ChatMessage> messages)
    {
        if (string.IsNullOrEmpty(userId) || messages.Count == 0) return;

        var filter = Builders<ChatSession>.Filter.Eq(s => s.UserId, userId);
        var session = await _db.ChatSessions.Find(filter).FirstOrDefaultAsync();
        if (session is null)
        {
            await _db.ChatSessions.InsertOneAsync(new ChatSession { UserId = userId, Messages = [.. messages] });
            return;
        }

        session.Messages ??= [];
        session.Messages.AddRange(messages);
        // Keep a bounded rolling window so a conversation can't grow without limit.
        const int maxMessages = 200;
        if (session.Messages.Count > maxMessages)
            session.Messages = session.Messages[^maxMessages..];
        await _db.ChatSessions.ReplaceOneAsync(filter, session);
    }

    public async Task ClearHistoryAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        await _db.ChatSessions.DeleteManyAsync(s => s.UserId == userId);
    }

    private static string BuildSystemPrompt(UserProfile? profile)
    {
        if (profile == null)
            return "You are Radar, a personal intelligence assistant. Help users learn, discover opportunities, and grow professionally. Be concise, insightful, and actionable.";

        return $"""
            You are Radar, a personal intelligence assistant for {profile.Name}.

            Their profile:
            - Persona: {profile.Persona}
            - Primary goal: {profile.PrimaryGoal}
            - Interests: {string.Join(", ", profile.Interests)}
            - Interest context: {string.Join("; ", InterestPersonalization.Contexts(profile).Select(c => $"{c.Interest}: {c.Level}, goal={c.Goal}, focus={c.Lens}"))}
            - Region: {profile.City}, {profile.Region}

            Your role: help them learn efficiently, surface relevant opportunities, and take concrete next steps toward their goal.

            Guidelines:
            - Be concise, specific, and actionable
            - Reference the relevant interest context, level, goal and focus when recommending resources
            - Write in clean, plain text. Do NOT use markdown: no # headings, no **bold**, no *italics*, no backticks. For lists, start each line with "• ". For sections, put a short title on its own line followed by a colon.
            - Keep formatting simple so answers read naturally in a chat bubble
            - Never give generic advice — always tie it to their situation
            """;
    }
}

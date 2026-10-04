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

        return new ChatMessage { Role = "assistant", Content = fullResponse.ToString() };
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
            - Use markdown for structure when helpful
            - Never give generic advice — always tie it to their situation
            """;
    }
}

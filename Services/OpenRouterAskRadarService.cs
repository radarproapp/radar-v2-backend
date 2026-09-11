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

    public OpenRouterAskRadarService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        IUserSessionService session)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _session = session;
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
        var systemPrompt = BuildSystemPrompt(profile);

        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var h in history)
            messages.Add(new { role = h.Role, content = h.Content });
        messages.Add(new { role = "user", content = message });

        var body = JsonSerializer.Serialize(new
        {
            model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
            messages,
            stream = true
        });

        var client = _httpFactory.CreateClient("OpenRouter");
        var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        string? connectionError = null;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            connectionError = "I'm having trouble connecting right now. Please try again in a moment.";
        }

        if (connectionError != null)
        {
            yield return connectionError;
            yield break;
        }

        await using var stream = await response!.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string? chunk = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                chunk = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta")
                    .GetProperty("content")
                    .GetString();
            }
            catch { }

            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
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
            - Region: {profile.City}, {profile.Region}

            Your role: help them learn efficiently, surface relevant opportunities, and take concrete next steps toward their goal.

            Guidelines:
            - Be concise, specific, and actionable
            - Reference their goal and interests when recommending resources
            - Use markdown for structure when helpful
            - Never give generic advice — always tie it to their situation
            """;
    }
}

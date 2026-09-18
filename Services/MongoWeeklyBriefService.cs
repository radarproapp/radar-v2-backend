using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoWeeklyBriefService : IWeeklyBriefService
{
    private readonly RadarDatabase _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MongoWeeklyBriefService> _log;

    public MongoWeeklyBriefService(
        RadarDatabase db,
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<MongoWeeklyBriefService> log)
    {
        _db     = db;
        _http   = http;
        _config = config;
        _log    = log;
    }

    public async Task<WeeklyBrief?> GetLatestBriefAsync(string userId)
    {
        var weekStart = CurrentWeekStart();
        var brief = await _db.WeeklyBriefs
            .Find(b => b.UserId == userId && b.WeekOf >= weekStart)
            .SortByDescending(b => b.WeekOf)
            .FirstOrDefaultAsync();

        return brief;
    }

    public async Task<WeeklyBrief> GenerateBriefAsync(string userId, UserProfile profile)
    {
        // Return cached brief if one exists for this week
        var existing = await GetLatestBriefAsync(userId);
        if (existing is not null) return existing;

        // Pull top content items from user's interest layers — news/articles/blog posts/videos/
        // podcasts only. No opportunities or research papers: those belong to Opportunities and
        // Learn Hub/Library respectively, not the brief.
        var layers       = InterestLayerMapper.MapInterestsToLayers(profile.Interests);
        var layerFilter  = layers.Count > 0
            ? Builders<ContentItem>.Filter.In(i => i.Layer, layers)
            : Builders<ContentItem>.Filter.Empty;

        var articles = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.Eq(i => i.Type, ContentType.Article) & layerFilter)
            .SortByDescending(i => i.PublishedAt).Limit(5).ToListAsync();

        var blogPosts = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.Eq(i => i.Type, ContentType.Essay) & layerFilter)
            .SortByDescending(i => i.PublishedAt).Limit(3).ToListAsync();

        var videos = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.Eq(i => i.Type, ContentType.Video) & layerFilter)
            .SortByDescending(i => i.PublishedAt).Limit(3).ToListAsync();

        var podcasts = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.Eq(i => i.Type, ContentType.Podcast) & layerFilter)
            .SortByDescending(i => i.PublishedAt).Limit(3).ToListAsync();

        // Generate AI recommendation text
        var recommendation = await GenerateRecommendationAsync(profile, articles, blogPosts);

        var brief = new WeeklyBrief
        {
            UserId               = userId,
            WeekOf               = CurrentWeekStart(),
            WeeklyRecommendation = recommendation,
            TopArticles          = [.. articles.Take(3), .. blogPosts.Take(2)],
            TopVideos            = videos.Take(2).ToList(),
            TopPodcasts          = podcasts.Take(2).ToList(),
        };

        try { await _db.WeeklyBriefs.InsertOneAsync(brief); }
        catch (Exception ex) { _log.LogWarning("Failed to persist weekly brief: {Message}", ex.Message); }

        return brief;
    }

    // ── AI recommendation ────────────────────────────────────────────────────

    private async Task<string> GenerateRecommendationAsync(
        UserProfile profile,
        List<ContentItem> articles,
        List<ContentItem> blogPosts)
    {
        var apiKey = _config["OpenRouter:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return FallbackRecommendation(profile);

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are Radar, an intelligence platform for ambitious young Africans. The user's primary goal is: \"{profile.PrimaryGoal}\". Their interests: {string.Join(", ", profile.Interests.Take(4))}.");
            sb.AppendLine();
            sb.AppendLine("Here are the top signals from this week's content:");
            foreach (var a in articles.Take(4))
                sb.AppendLine($"- {a.Signal} ({a.Source})");
            foreach (var b in blogPosts.Take(2))
                sb.AppendLine($"- {b.Signal} ({b.Source})");
            sb.AppendLine();
            sb.AppendLine("Write a 2-3 sentence weekly intelligence recommendation. It should: (1) connect the week's top signal to the user's goal, (2) give one concrete action they can take this week, (3) end with a short motivating thought. Write in second person, confident and direct. No bullet points — flowing prose only.");

            var client = _http.CreateClient("OpenRouter");
            var payload = new
            {
                model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
                messages = new[] { new { role = "user", content = sb.ToString() } },
                max_tokens = 200,
                temperature = 0.7
            };

            using var response = await client.PostAsync("chat/completions",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode) return FallbackRecommendation(profile);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return text?.Trim() ?? FallbackRecommendation(profile);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Weekly brief AI generation failed: {Message}", ex.Message);
            return FallbackRecommendation(profile);
        }
    }

    private static string FallbackRecommendation(UserProfile profile) =>
        $"This week's signals point directly at your goal: {profile.PrimaryGoal}. " +
        "Pick one article from the feed, read it fully, and write three sentences about what it changes in how you think. " +
        "Small consistent action compounds faster than any single big move.";

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DateTime CurrentWeekStart()
    {
        var today = DateTime.UtcNow.Date;
        var diff  = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        return today.AddDays(-diff);
    }

}

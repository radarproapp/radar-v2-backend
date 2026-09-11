using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Generates daily signal clips from the user's intelligence feed.
/// Clips are curated highlights — the most important signals for the day.
/// </summary>
public class MongoClipsService : IClipsService
{
    private readonly RadarDatabase _db;

    public MongoClipsService(RadarDatabase db) => _db = db;

    public async Task<List<Clip>> GetDailyClipsAsync(UserProfile profile)
    {
        // Check if clips already exist for today
        var today = DateTime.UtcNow.Date;
        var existingClips = await _db.Clips
            .Find(c => c.UserId == profile.Id && c.PublishedAt >= today)
            .ToListAsync();

        if (existingClips.Count > 0)
            return existingClips;

        // Generate clips from the user's intelligence feed
        var clips = await GenerateClipsFromFeedAsync(profile);

        // Persist them
        if (clips.Count > 0)
            await _db.Clips.InsertManyAsync(clips);

        return clips;
    }

    public async Task SaveClipAsync(string userId, string clipId)
    {
        var clip = await _db.Clips.Find(c => c.Id == clipId).FirstOrDefaultAsync();
        if (clip is null) return;

        clip.IsSaved = !clip.IsSaved;
        await _db.Clips.ReplaceOneAsync(c => c.Id == clipId, clip);
    }

    private async Task<List<Clip>> GenerateClipsFromFeedAsync(UserProfile profile)
    {
        // Get the user's top content items from the intelligence feed
        var layers = MapInterestsToLayers(profile.Interests);

        var filter = layers.Count > 0
            ? Builders<ContentItem>.Filter.In(i => i.Layer, layers)
            : Builders<ContentItem>.Filter.Empty;

        var items = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .Limit(20)
            .ToListAsync();

        var clips = new List<Clip>();
        var tagPool = new[] { "Technology", "Number of the day", "Payments", "Research", "One idea", "Policy", "Career", "AI" };

        foreach (var item in items.Take(5))
        {
            var clip = new Clip
            {
                UserId = profile.Id,
                Tag = tagPool[clips.Count % tagPool.Length],
                Signal = item.Signal ?? item.Title,
                WhyItMatters = item.WhyItMatters ?? item.AiSummary ?? "",
                Source = $"{item.Source} · {GetTimeAgo(item.PublishedAt)}",
                ContentItemId = item.Id,
                PublishedAt = item.PublishedAt,
            };
            clips.Add(clip);
        }

        return clips;
    }

    private static string GetTimeAgo(DateTime date)
    {
        var diff = DateTime.UtcNow - date;
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return date.ToString("MMM d");
    }

    private static List<ContentLayer> MapInterestsToLayers(List<string> interests)
    {
        if (interests.Count == 0) return [];
        var layers = new HashSet<ContentLayer>();
        foreach (var interest in interests)
        {
            var norm = interest.Trim().ToLowerInvariant();
            ContentLayer[] mapped = norm switch
            {
                var s when s.Contains("tech") || s.Contains("ai") || s.Contains("software")
                    => [ContentLayer.Ideas, ContentLayer.Science],
                var s when s.Contains("business") || s.Contains("entrepreneur") || s.Contains("startup")
                    => [ContentLayer.Ideas, ContentLayer.Finance, ContentLayer.Career],
                var s when s.Contains("finance") || s.Contains("invest") || s.Contains("money") || s.Contains("banking")
                    => [ContentLayer.Finance],
                var s when s.Contains("policy") || s.Contains("governance") || s.Contains("politi")
                    => [ContentLayer.Policy],
                var s when s.Contains("health") || s.Contains("medicine") || s.Contains("medical")
                    => [ContentLayer.Medicine],
                var s when s.Contains("climate") || s.Contains("environment") || s.Contains("green")
                    => [ContentLayer.Environment],
                var s when s.Contains("science") || s.Contains("research") || s.Contains("academic")
                    => [ContentLayer.Science, ContentLayer.Academic],
                _ => [],
            };
            foreach (var l in mapped) layers.Add(l);
        }
        return [.. layers];
    }
}

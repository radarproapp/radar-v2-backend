using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoSourceService : ISourceService
{
    private readonly RadarDatabase _db;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoSourceService(RadarDatabase db) => _db = db;

    public async Task<SourceProfile?> GetSourceAsync(string sourceId)
    {
        await EnsureSeededAsync();
        return await _db.Sources.Find(s => s.Id == sourceId).FirstOrDefaultAsync();
    }

    public async Task<List<SourceProfile>> GetSourcesAsync(string userId)
    {
        await EnsureSeededAsync();
        return await _db.Sources.Find(_ => true)
            .SortBy(s => s.Name)
            .ToListAsync();
    }

    public async Task ToggleFollowAsync(string userId, string sourceId)
    {
        var source = await _db.Sources.Find(s => s.Id == sourceId).FirstOrDefaultAsync();
        if (source is null) return;
        source.IsFollowing = !source.IsFollowing;
        await _db.Sources.ReplaceOneAsync(s => s.Id == sourceId, source);
    }

    public async Task ToggleWeeklyBriefAsync(string userId, string sourceId)
    {
        var source = await _db.Sources.Find(s => s.Id == sourceId).FirstOrDefaultAsync();
        if (source is null) return;
        source.IncludeInWeeklyBrief = !source.IncludeInWeeklyBrief;
        await _db.Sources.ReplaceOneAsync(s => s.Id == sourceId, source);
    }

    public async Task TogglePrioritiseAsync(string userId, string sourceId)
    {
        var source = await _db.Sources.Find(s => s.Id == sourceId).FirstOrDefaultAsync();
        if (source is null) return;
        source.PrioritiseInFeed = !source.PrioritiseInFeed;
        await _db.Sources.ReplaceOneAsync(s => s.Id == sourceId, source);
    }

    private async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            var count = await _db.Sources.CountDocumentsAsync(Builders<SourceProfile>.Filter.Empty);
            if (count == 0)
            {
                await _db.Sources.InsertManyAsync(SourceSeedData.All);
            }
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }
}

public class MongoTopicService : ITopicService
{
    private readonly RadarDatabase _db;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoTopicService(RadarDatabase db) => _db = db;

    public async Task<TopicProfile?> GetTopicAsync(string topicId)
    {
        await EnsureSeededAsync();
        return await _db.Topics.Find(t => t.Id == topicId).FirstOrDefaultAsync();
    }

    public async Task<List<TopicProfile>> GetTopicsAsync(string userId)
    {
        await EnsureSeededAsync();
        return await _db.Topics.Find(_ => true)
            .SortByDescending(t => t.ItemsInRadar)
            .ToListAsync();
    }

    public async Task ToggleFollowAsync(string userId, string topicId)
    {
        var topic = await _db.Topics.Find(t => t.Id == topicId).FirstOrDefaultAsync();
        if (topic is null) return;
        topic.IsFollowing = !topic.IsFollowing;
        await _db.Topics.ReplaceOneAsync(t => t.Id == topicId, topic);
    }

    public async Task ToggleAlertAsync(string userId, string topicId, int alertIndex)
    {
        var topic = await _db.Topics.Find(t => t.Id == topicId).FirstOrDefaultAsync();
        if (topic is null || alertIndex < 0 || alertIndex >= topic.Alerts.Count) return;
        topic.Alerts[alertIndex].IsEnabled = !topic.Alerts[alertIndex].IsEnabled;
        await _db.Topics.ReplaceOneAsync(t => t.Id == topicId, topic);
    }

    private async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            var count = await _db.Topics.CountDocumentsAsync(Builders<TopicProfile>.Filter.Empty);
            if (count == 0)
            {
                await _db.Topics.InsertManyAsync(SourceSeedData.Topics);
            }
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }
}

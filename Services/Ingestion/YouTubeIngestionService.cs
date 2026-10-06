using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Ingests the configured YouTube channels: resolves each handle to a channel id (cached in Mongo),
/// then reads the channel's public Atom feed into feed items.
/// </summary>
public class YouTubeIngestionService
{
    private readonly RadarDatabase _db;
    private readonly YouTubeClient _client;
    private readonly ILogger<YouTubeIngestionService> _log;

    public YouTubeIngestionService(RadarDatabase db, YouTubeClient client, ILogger<YouTubeIngestionService> log)
    {
        _db = db;
        _client = client;
        _log = log;
    }

    public async Task<List<RawFeedItem>> FetchAllAsync(CancellationToken ct)
    {
        var all = new List<RawFeedItem>();
        foreach (var source in YouTubeSourceRegistry.All)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var channelId = await GetOrResolveAsync(source, ct);
                if (channelId is null)
                {
                    _log.LogWarning("YouTube: could not resolve channel for {Name} ({Handle})", source.Name, source.Handle);
                    continue;
                }
                all.AddRange(await _client.FetchChannelFeedAsync(source, channelId, ct));
            }
            catch (Exception ex)
            {
                _log.LogWarning("YouTube fetch failed for {Name}: {Message}", source.Name, ex.Message);
            }
        }
        return all;
    }

    private async Task<string?> GetOrResolveAsync(YouTubeSource source, CancellationToken ct)
    {
        var cached = await _db.YouTubeChannels.Find(c => c.Handle == source.Handle).FirstOrDefaultAsync(ct);
        if (cached is not null) return cached.ChannelId;

        var channelId = await _client.ResolveChannelIdAsync(source.Handle, ct);
        if (channelId is null) return null;

        await _db.YouTubeChannels.InsertOneAsync(
            new YouTubeChannelDoc { ChannelId = channelId, Handle = source.Handle, Name = source.Name },
            cancellationToken: ct);
        _log.LogInformation("YouTube: resolved {Handle} to {ChannelId}", source.Handle, channelId);
        return channelId;
    }
}

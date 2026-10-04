using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Persists raw user/ranking events. Deliberately dumb — aggregation and quality analysis
/// (weekly best/worst review, "why helpful rate", ranking-weight tuning) happen out of band
/// against this collection, not here. See §3.1/§3.2 of the product-strategy notes.
/// </summary>
public class MongoAnalyticsService : IAnalyticsService
{
    private readonly RadarDatabase _db;
    private readonly IBehavioralSignalService _signals;

    public MongoAnalyticsService(RadarDatabase db, IBehavioralSignalService signals)
    {
        _db = db;
        _signals = signals;
    }

    public async Task LogEventAsync(AnalyticsEvent evt)
    {
        await _db.AnalyticsEvents.InsertOneAsync(evt);

        if (string.IsNullOrWhiteSpace(evt.UserId)) return;

        evt.Metadata.TryGetValue("interest", out var interest);
        var profile = await _db.Profiles.Find(p => p.Id == evt.UserId).FirstOrDefaultAsync();
        if (profile is null) return;

        ContentItem? contentItem = null;
        if (string.IsNullOrWhiteSpace(interest) && !string.IsNullOrWhiteSpace(evt.ContentItemId))
        {
            contentItem = await _db.ContentItems.Find(i => i.Id == evt.ContentItemId).FirstOrDefaultAsync();
            var text = contentItem is null ? string.Empty : $"{contentItem.Title} {contentItem.Topic} {contentItem.Signal} {string.Join(' ', contentItem.Tags)}";
            interest = InterestPersonalization.Contexts(profile)
                .Select(context => context.Interest)
                .FirstOrDefault(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        // Update the legacy per-interest activity counter when we can attribute an interest.
        if (!string.IsNullOrWhiteSpace(interest))
        {
            var weight = evt.Type switch
            {
                AnalyticsEventType.Save or AnalyticsEventType.AddToRoadmap => 4,
                AnalyticsEventType.Open => 2,
                AnalyticsEventType.Impression => 1,
                _ => 0
            };
            if (weight > 0)
            {
                profile.InterestActivityScores[interest] = profile.InterestActivityScores.GetValueOrDefault(interest) + weight;
                await _db.Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);
            }
        }

        // Persist the evolving behavioural signal memory (declared + inferred, decayed at read time).
        // Term precedence: an interest we can attribute the event to, then an explicit term the client
        // sent (e.g. a search query), then the item's own topic so un-attributed reads still accumulate.
        var action = MapAction(evt.Type);
        if (action is not null)
        {
            var term = !string.IsNullOrWhiteSpace(interest)
                ? interest!
                : evt.Metadata.GetValueOrDefault("term");
            if (string.IsNullOrWhiteSpace(term))
                term = contentItem?.Topic;

            if (!string.IsNullOrWhiteSpace(term))
                await _signals.RecordAsync(evt.UserId, term!, action.Value);
        }
    }

    private static BehavioralAction? MapAction(AnalyticsEventType type) => type switch
    {
        AnalyticsEventType.MoreLikeThis => BehavioralAction.MoreLikeThis,
        AnalyticsEventType.Save => BehavioralAction.Save,
        AnalyticsEventType.Complete => BehavioralAction.Complete,
        AnalyticsEventType.Applied => BehavioralAction.Apply,
        AnalyticsEventType.Search => BehavioralAction.Search,
        AnalyticsEventType.Read => BehavioralAction.Read,
        AnalyticsEventType.Open => BehavioralAction.Open,
        AnalyticsEventType.LessLikeThis => BehavioralAction.LessLikeThis,
        AnalyticsEventType.NotRelevant => BehavioralAction.NotRelevant,
        // Opting a topic into the roadmap is the strongest "keep this coming" signal we get from
        // reading a single item, so it weighs the same as an explicit "more like this".
        AnalyticsEventType.AddToRoadmap => BehavioralAction.MoreLikeThis,
        AnalyticsEventType.ClipSaved => BehavioralAction.Save,
        // Un-saving partly walks back the earlier save rather than being a full rejection.
        AnalyticsEventType.Unsave => BehavioralAction.Skip,
        AnalyticsEventType.CaptureCreated => BehavioralAction.Read,
        _ => null
    };

    public async Task<Dictionary<AnalyticsEventType, long>> GetEventCountsAsync(int trailingDays = 7)
    {
        var since = DateTime.UtcNow.AddDays(-trailingDays);
        var counts = new Dictionary<AnalyticsEventType, long>();

        foreach (var type in Enum.GetValues<AnalyticsEventType>())
        {
            counts[type] = await _db.AnalyticsEvents.CountDocumentsAsync(
                e => e.Type == type && e.CreatedAt >= since);
        }

        return counts;
    }
}

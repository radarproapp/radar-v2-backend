using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Assembles everything Radar can honestly say a user has done.
/// </summary>
/// <remarks>
/// Read-time aggregation, deliberately. <c>UserProfile.Stats</c> already declared fields for most
/// of this and nothing ever incremented them, so the Growth Tracker showed nine tiles of zero to
/// every user who ever opened it. Adding writes to those counters would have fixed the display and
/// left a new way for the display to be wrong; deriving them means the page cannot disagree with
/// the rows it is describing.
///
/// The cost is a handful of queries per page view. That is a page somebody opens occasionally,
/// not the feed.
/// </remarks>
public class UserGraphService
{
    private readonly RadarDatabase _db;

    public UserGraphService(RadarDatabase db) => _db = db;

    /// <summary>How many entries the "top" lists carry. Enough to be a picture, few enough to read.</summary>
    private const int TopN = 6;

    public async Task<UserGraph> BuildAsync(string userId, CancellationToken ct = default)
    {
        var graph = new UserGraph();
        if (string.IsNullOrWhiteSpace(userId)) return graph;

        var events = await _db.AnalyticsEvents
            .Find(e => e.UserId == userId)
            .ToListAsync(ct);

        var playback = await _db.PlaybackProgress
            .Find(p => p.UserId == userId)
            .ToListAsync(ct);

        // Only the items those rows actually refer to. Loading the corpus to summarise a dozen
        // interactions is how a profile page becomes the slowest screen in the product.
        var itemIds = events
            .Select(e => e.ContentItemId)
            .Concat(playback.Select(p => p.ContentItemId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var items = itemIds.Count == 0
            ? []
            : await _db.ContentItems
                .Find(Builders<ContentItem>.Filter.In(c => c.Id, itemIds))
                .Project(c => new ContentItem
                {
                    Id = c.Id,
                    Title = c.Title,
                    Source = c.Source,
                    Topic = c.Topic,
                    Type = c.Type,
                })
                .ToListAsync(ct);

        var byId = items.ToDictionary(i => i.Id, StringComparer.Ordinal);

        BuildListening(graph, playback, byId);
        BuildReading(graph, events, byId);
        await BuildLearningAsync(graph, userId, ct);
        await BuildOpportunitiesAsync(graph, userId, ct);
        BuildTopics(graph, events, playback, byId);
        BuildSources(graph, events, byId);
        BuildMonths(graph, events, playback);

        var stamps = events.Select(e => e.CreatedAt)
            .Concat(playback.Select(p => p.StartedAt))
            .ToList();

        if (stamps.Count > 0)
        {
            graph.FirstActivityAt = stamps.Min();
            graph.LastActivityAt = stamps.Max();
        }

        return graph;
    }

    private static void BuildListening(
        UserGraph graph, List<Data.Documents.PlaybackProgressDoc> playback, Dictionary<string, ContentItem> byId)
    {
        graph.Listening.SecondsPlayed = playback.Sum(p => p.SecondsPlayed);

        foreach (var row in playback)
        {
            // An item whose row we have but whose content is gone — dismissed, purged as a
            // duplicate — still counts towards time played, because the listening happened. It
            // just cannot be attributed to a type.
            if (!byId.TryGetValue(row.ContentItemId, out var item)) continue;

            var finished = row.CompletedAt is not null;

            if (item.Type == ContentType.Podcast)
            {
                graph.Listening.EpisodesStarted++;
                if (finished) graph.Listening.EpisodesFinished++;
            }
            else
            {
                graph.Listening.VideosStarted++;
                if (finished) graph.Listening.VideosFinished++;
            }
        }

        // The one they gave the most time to, which is not always the one they finished.
        var top = playback
            .Where(p => p.SecondsPlayed > 0 && byId.ContainsKey(p.ContentItemId))
            .OrderByDescending(p => p.SecondsPlayed)
            .FirstOrDefault();

        if (top is null) return;

        var topItem = byId[top.ContentItemId];
        graph.Listening.MostPlayed = new GraphItem
        {
            Id = topItem.Id,
            Title = topItem.Title,
            Source = topItem.Source,
            Topic = topItem.Topic,
            Type = topItem.Type,
            SecondsPlayed = top.SecondsPlayed,
        };
    }

    private static void BuildReading(
        UserGraph graph, List<AnalyticsEvent> events, Dictionary<string, ContentItem> byId)
    {
        // Opens, counted per item rather than per event: opening the same brief four times is one
        // thing read, and counting it four times is how a number stops meaning anything.
        graph.Reading.ItemsOpened = events
            .Where(e => e.Type is AnalyticsEventType.Open or AnalyticsEventType.Read)
            .Select(e => e.ContentItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();

        // Saves net of unsaves, because a saved-then-unsaved item is not saved.
        var saved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in events.OrderBy(e => e.CreatedAt))
        {
            if (string.IsNullOrWhiteSpace(e.ContentItemId)) continue;
            if (e.Type == AnalyticsEventType.Save) saved.Add(e.ContentItemId);
            else if (e.Type == AnalyticsEventType.Unsave) saved.Remove(e.ContentItemId);
        }
        graph.Reading.ItemsSaved = saved.Count;

        graph.Reading.PapersOpened = events
            .Where(e => e.Type is AnalyticsEventType.Open or AnalyticsEventType.Read)
            .Select(e => e.ContentItemId)
            .Where(id => id is not null && byId.TryGetValue(id, out var i) && i.Type == ContentType.ResearchPaper)
            .Distinct(StringComparer.Ordinal)
            .Count();

        graph.Reading.CapturesMade = events.Count(e => e.Type == AnalyticsEventType.CaptureCreated);
    }

    private async Task BuildLearningAsync(UserGraph graph, string userId, CancellationToken ct)
    {
        var roadmaps = await _db.Roadmaps.Find(r => r.UserId == userId).ToListAsync(ct);

        foreach (var module in roadmaps.SelectMany(r => r.Modules ?? []))
        {
            graph.Learning.PathSections++;
            if (!string.IsNullOrWhiteSpace(module.Title)) graph.Learning.Subjects.Add(module.Title);

            var lessons = module.Lessons ?? [];
            graph.Learning.LessonsTotal += lessons.Count;
            graph.Learning.LessonsCompleted += lessons.Count(l => l.IsCompleted);
        }

        graph.Learning.ExercisesCompleted = roadmaps
            .SelectMany(r => r.CompletedExerciseIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .Count();

        var quizzes = await _db.MentorQuizzes.Find(q => q.UserId == userId).ToListAsync(ct);
        var complete = quizzes.Where(q => q.IsComplete).ToList();

        graph.Learning.QuizzesTaken = complete.Count;

        // Null rather than zero when nothing is finished. A zero average reads as "you scored
        // nothing", which is a different claim from "you have not taken one yet".
        if (complete.Count > 0)
            graph.Learning.AverageQuizScore = (int)Math.Round(complete.Average(q => q.Score));

        graph.Learning.WeakAreas = complete
            .SelectMany(q => q.DetectedWeakAreas ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(TopN)
            .ToList();

        graph.Learning.ProjectsCompleted = (int)await _db.UserProjects
            .CountDocumentsAsync(p => p.UserId == userId && p.IsCompleted, cancellationToken: ct);

        graph.Reading.NotesWritten = (int)await _db.Notes
            .CountDocumentsAsync(n => n.UserId == userId, cancellationToken: ct);
    }

    private async Task BuildOpportunitiesAsync(UserGraph graph, string userId, CancellationToken ct)
    {
        graph.Opportunities.Saved = (int)await _db.SavedOpportunities
            .CountDocumentsAsync(s => s.UserId == userId, cancellationToken: ct);

        graph.Opportunities.Applied = (int)await _db.AppliedOpportunities
            .CountDocumentsAsync(a => a.UserId == userId, cancellationToken: ct);

        var tracking = await _db.OpportunityTrackings
            .Find(t => t.UserId == userId)
            .ToListAsync(ct);

        graph.Opportunities.ByStatus = tracking
            .Where(t => t.Status != OpportunityTrackingStatus.None)
            .GroupBy(t => t.Status.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static void BuildTopics(
        UserGraph graph,
        List<AnalyticsEvent> events,
        List<Data.Documents.PlaybackProgressDoc> playback,
        Dictionary<string, ContentItem> byId)
    {
        var interactions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Impressions excluded. Being shown something is not attention, and including it would
        // rank the topic Radar pushed hardest rather than the one the reader chose.
        foreach (var e in events.Where(e => e.Type != AnalyticsEventType.Impression))
        {
            if (e.ContentItemId is null || !byId.TryGetValue(e.ContentItemId, out var item)) continue;
            if (string.IsNullOrWhiteSpace(item.Topic)) continue;

            interactions[item.Topic] = interactions.GetValueOrDefault(item.Topic) + 1;
        }

        foreach (var row in playback.Where(p => p.SecondsPlayed > 0))
        {
            if (!byId.TryGetValue(row.ContentItemId, out var item)) continue;
            if (string.IsNullOrWhiteSpace(item.Topic)) continue;

            seconds[item.Topic] = seconds.GetValueOrDefault(item.Topic) + row.SecondsPlayed;
        }

        graph.TopTopics = interactions.Keys
            .Concat(seconds.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(topic => new GraphTopic
            {
                Topic = topic,
                Interactions = interactions.GetValueOrDefault(topic),
                SecondsPlayed = seconds.GetValueOrDefault(topic),
            })
            // Time first, because a listened hour is stronger evidence than six clicks.
            .OrderByDescending(t => t.SecondsPlayed)
            .ThenByDescending(t => t.Interactions)
            .Take(TopN)
            .ToList();
    }

    private static void BuildSources(
        UserGraph graph, List<AnalyticsEvent> events, Dictionary<string, ContentItem> byId)
    {
        graph.TopSources = events
            .Where(e => e.Type is AnalyticsEventType.Open or AnalyticsEventType.Read)
            .Select(e => e.ContentItemId)
            .Where(id => id is not null && byId.ContainsKey(id))
            .Select(id => byId[id!].Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .GroupBy(source => source!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GraphSource { Source = g.Key, Opens = g.Count() })
            .OrderByDescending(s => s.Opens)
            .Take(TopN)
            .ToList();
    }

    private static void BuildMonths(
        UserGraph graph, List<AnalyticsEvent> events, List<Data.Documents.PlaybackProgressDoc> playback)
    {
        var months = new Dictionary<DateTime, GraphMonth>();

        GraphMonth Bucket(DateTime stamp)
        {
            var key = new DateTime(stamp.Year, stamp.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            if (!months.TryGetValue(key, out var bucket))
            {
                bucket = new GraphMonth { Month = key };
                months[key] = bucket;
            }
            return bucket;
        }

        foreach (var e in events.Where(e => e.Type != AnalyticsEventType.Impression))
            Bucket(e.CreatedAt).Interactions++;

        // Attributed to when the item was last played, which is the only date the row carries.
        // A long episode listened across two months lands entirely in the second one; the
        // alternative is inventing a distribution.
        foreach (var row in playback.Where(p => p.SecondsPlayed > 0))
            Bucket(row.UpdatedAt).SecondsPlayed += row.SecondsPlayed;

        graph.Months = months.Values.OrderBy(m => m.Month).ToList();
    }
}

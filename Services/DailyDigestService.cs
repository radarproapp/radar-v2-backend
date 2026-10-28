using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// The daily message: what you were working on, and what has arrived in it since.
/// </summary>
/// <remarks>
/// The rule this is built around: <b>send nothing when there is nothing to say.</b>
///
/// A daily email that arrives every day regardless is trained into the archive folder within a
/// fortnight, and it takes the password reset and everything else from the same domain with it.
/// So a digest is assembled only when the corpus has something new in a topic the reader has
/// actually touched, and a reader with nothing new gets silence rather than a message explaining
/// that nothing happened.
///
/// What goes in it comes from <see cref="UserGraphService"/> rather than a second notion of
/// activity. The graph is already the honest account of what somebody has done — deriving it
/// twice is how two screens start disagreeing about the same person.
/// </remarks>
public class DailyDigestService
{
    private readonly RadarDatabase _db;
    private readonly UserGraphService _graphs;
    private readonly ILogger<DailyDigestService> _log;

    public DailyDigestService(RadarDatabase db, UserGraphService graphs, ILogger<DailyDigestService> log)
    {
        _db = db;
        _graphs = graphs;
        _log = log;
    }

    /// <summary>How many new items to name. More than three is a list nobody reads.</summary>
    private const int Highlights = 3;

    /// <summary>
    /// What a digest says. Null when there is not enough to be worth an email.
    /// </summary>
    public sealed class Digest
    {
        public required string Email { get; init; }
        public required string FirstName { get; init; }

        /// <summary>New items in topics this reader has actually touched.</summary>
        public List<ContentItem> New { get; init; } = [];

        /// <summary>Where they left off, when they left off mid-something.</summary>
        public ContentItem? Resume { get; init; }
        public double ResumePositionSeconds { get; init; }

        /// <summary>The next unfinished lesson on their path, if they have one.</summary>
        public string? NextLesson { get; init; }
        public string? NextLessonSection { get; init; }

        /// <summary>Applications with a deadline inside a week.</summary>
        public List<Opportunity> Closing { get; init; } = [];
    }

    /// <summary>
    /// Builds a digest for one profile, or returns null.
    /// </summary>
    public async Task<Digest?> BuildAsync(UserProfile profile, string email, CancellationToken ct = default)
    {
        // Somebody who has not said what they follow has no topics to collect against, and a
        // digest of the general corpus is a newsletter nobody subscribed to.
        if (!profile.IsPersonalised) return null;

        var graph = await _graphs.BuildAsync(profile.Id, ct);

        // Their own topics, by what they have actually spent attention on — not by what they
        // ticked at signup, which is why the graph is the source rather than the profile.
        var topics = graph.TopTopics.Select(t => t.Topic).ToList();
        if (topics.Count == 0) topics = profile.Interests.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        if (topics.Count == 0) return null;

        var since = DateTime.UtcNow.AddDays(-1);

        var seen = await _db.AnalyticsEvents
            .Find(e => e.UserId == profile.Id && e.ContentItemId != null)
            .Project(e => e.ContentItemId!)
            .ToListAsync(ct);

        var seenIds = seen.ToHashSet(StringComparer.Ordinal);

        var fresh = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.And(
                Builders<ContentItem>.Filter.Gte(c => c.PublishedAt, since),
                Builders<ContentItem>.Filter.In(c => c.Topic, topics)))
            .SortByDescending(c => c.PublishedAt)
            .Limit(Highlights * 4)
            .ToListAsync(ct);

        // Nothing they have already opened. A digest that re-offers yesterday's reading is how a
        // reader learns it is not paying attention.
        var newItems = fresh.Where(f => !seenIds.Contains(f.Id)).Take(Highlights).ToList();

        var (resume, position) = await ResumeAsync(profile.Id, ct);
        var (lesson, section) = await NextLessonAsync(profile.Id, ct);
        var closing = await ClosingAsync(profile.Id, ct);

        // The threshold. One unfinished episode on its own is not news — they know they paused
        // it. Something genuinely new, or a deadline that is about to pass, is.
        if (newItems.Count == 0 && closing.Count == 0) return null;

        return new Digest
        {
            Email = email,
            FirstName = profile.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there",
            New = newItems,
            Resume = resume,
            ResumePositionSeconds = position,
            NextLesson = lesson,
            NextLessonSection = section,
            Closing = closing,
        };
    }

    /// <summary>The thing they were part-way through, if they did not finish it.</summary>
    private async Task<(ContentItem?, double)> ResumeAsync(string userId, CancellationToken ct)
    {
        var row = await _db.PlaybackProgress
            .Find(p => p.UserId == userId && p.CompletedAt == null && p.SecondsPlayed > 60)
            .SortByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (row is null) return (null, 0);

        var item = await _db.ContentItems.Find(c => c.Id == row.ContentItemId).FirstOrDefaultAsync(ct);
        return (item, row.PositionSeconds);
    }

    private async Task<(string?, string?)> NextLessonAsync(string userId, CancellationToken ct)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync(ct);

        if (roadmap?.Modules is null) return (null, null);

        foreach (var module in roadmap.Modules.OrderBy(m => m.Order))
        {
            var next = (module.Lessons ?? []).OrderBy(l => l.Order).FirstOrDefault(l => !l.IsCompleted);
            if (next is not null) return (next.Title, module.Title);
        }

        return (null, null);
    }

    /// <summary>
    /// Opportunities they saved or applied for that close within a week.
    /// </summary>
    /// <remarks>
    /// Only ones they have shown an interest in. A deadline reminder for something they never
    /// looked at is an advert wearing a reminder's clothes.
    /// </remarks>
    private async Task<List<Opportunity>> ClosingAsync(string userId, CancellationToken ct)
    {
        var saved = await _db.SavedOpportunities
            .Find(s => s.UserId == userId)
            .Project(s => s.OpportunityId)
            .ToListAsync(ct);

        var applied = await _db.AppliedOpportunities
            .Find(a => a.UserId == userId)
            .Project(a => a.OpportunityId)
            .ToListAsync(ct);

        var ids = saved.Concat(applied).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return [];

        var soon = DateTime.UtcNow.AddDays(7);

        return await _db.Opportunities
            .Find(Builders<Opportunity>.Filter.And(
                Builders<Opportunity>.Filter.In(o => o.Id, ids),
                Builders<Opportunity>.Filter.Ne(o => o.Deadline, null),
                Builders<Opportunity>.Filter.Gt(o => o.Deadline, DateTime.UtcNow),
                Builders<Opportunity>.Filter.Lt(o => o.Deadline, soon)))
            .SortBy(o => o.Deadline)
            .Limit(3)
            .ToListAsync(ct);
    }
}

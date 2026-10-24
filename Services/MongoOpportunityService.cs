using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoOpportunityService : IOpportunityService
{
    private readonly RadarDatabase _db;
    private readonly AiOpportunityMatchService _aiMatch;
    private readonly IUserLearningRecordService _records;
    private readonly IBehavioralSignalService _signals;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoOpportunityService(
        RadarDatabase db,
        AiOpportunityMatchService aiMatch,
        IUserLearningRecordService records,
        IBehavioralSignalService signals)
    {
        _db = db;
        _aiMatch = aiMatch;
        _records = records;
        _signals = signals;
    }

    /// <summary>
    /// What the user's own behaviour says about them, or null when there is no profile to derive
    /// it from. Built once per request and handed to every opportunity being scored - it is
    /// derived on read, so scoring each item separately would rebuild it for each one.
    /// </summary>
    private async Task<UserLearningRecord?> RecordForAsync(UserProfile? profile)
        => profile is null ? null : await _records.BuildAsync(profile);

    /// <summary>Ceiling on how many opportunities are scored for one request.</summary>
    private const int MaxScoringCandidates = 500;

    public Task<List<Opportunity>> GetOpportunitiesAsync(
        UserProfile profile, OpportunityType? filterType = null, int page = 1, int pageSize = 20)
    {
        return GetOpportunitiesAsync(profile, filterType, null, null, null, null, page, pageSize);
    }

    /// <summary>
    /// Candidate set for per-user scoring. Ranking is user-specific and computed in memory, so the
    /// page itself cannot be pushed into Mongo — but the expiry filter and a hard ceiling can be,
    /// which is what stops this from degrading into "load the entire collection on every request".
    /// </summary>
    private async Task<List<Opportunity>> GetScoringCandidatesAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var notExpired = Builders<Opportunity>.Filter.Or(
            Builders<Opportunity>.Filter.Eq(o => o.Deadline, null),
            Builders<Opportunity>.Filter.Gte(o => o.Deadline, cutoff));

        return await _db.Opportunities
            .Find(notExpired)
            .Limit(MaxScoringCandidates)
            .ToListAsync();
    }

    public async Task<List<Opportunity>> GetOpportunitiesAsync(
        UserProfile profile,
        OpportunityType? filterType = null,
        string? region = null,
        string? goal = null,
        string? urgency = null,
        string? matchTier = null,
        int page = 1,
        int pageSize = 20)
    {
        await EnsureSeedAsync();

        var all = await GetScoringCandidatesAsync();

        // Reconcile user tracking & continuous learning weights
        var trackingMap = await GetTrackingMapAsync(profile.Id);
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);

        foreach (var opp in all)
        {
            EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord);
        }

        // Apply AI match refinement if enabled
        try
        {
            await _aiMatch.ApplyAsync(profile, all);
        }
        catch
        {
            // Fall back cleanly to deterministic scoring
        }

        // Filter out expired opportunities
        var now = DateTime.UtcNow;
        var valid = all.Where(o => !o.Deadline.HasValue || o.Deadline.Value >= now.AddDays(-1)).ToList();

        // Apply user continuous learning filter: Hide opportunities marked NotRelevant unless explicitly viewing tracked
        valid = valid.Where(o => o.UserTrackingStatus != OpportunityTrackingStatus.NotRelevant).ToList();

        // Apply filters
        if (filterType.HasValue)
        {
            valid = valid.Where(o => o.Type == filterType.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(region) && !region.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            valid = valid.Where(o =>
                o.RegionTag.Equals(region, StringComparison.OrdinalIgnoreCase) ||
                (o.Location != null && o.Location.Contains(region, StringComparison.OrdinalIgnoreCase)) ||
                (region.Equals("Remote", StringComparison.OrdinalIgnoreCase) && o.IsRemote)
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(goal) && !goal.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            valid = valid.Where(o => o.GoalTag.Equals(goal, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(urgency) && !urgency.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            valid = urgency.ToLowerInvariant() switch
            {
                "urgent" or "3days" => valid.Where(o => o.DaysUntilDeadline <= 7).ToList(),
                "2weeks" or "thismonth" => valid.Where(o => o.DaysUntilDeadline > 7 && o.DaysUntilDeadline <= 21).ToList(),
                "open" or "recent" => valid.Where(o => o.DaysUntilDeadline > 21).ToList(),
                _ => valid
            };
        }

        if (!string.IsNullOrWhiteSpace(matchTier) && !matchTier.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            valid = valid.Where(o => o.MatchTier.Equals(matchTier, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        valid.Sort((a, b) => b.MatchScorePercent.CompareTo(a.MatchScorePercent));

        return valid
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    /// <summary>
    /// Serves today's batch. Reads the precomputed one when the scheduled job has already built it,
    /// and builds on demand otherwise — a user who signs up between runs still gets a batch, and
    /// the feature does not silently depend on the cron being healthy.
    /// </summary>
    public async Task<DailyOpportunityFeed> GetDailyFeedAsync(UserProfile profile)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var stored = await _db.DailyOpportunityBatches
            .Find(d => d.Id == $"{profile.Id}:{today}")
            .FirstOrDefaultAsync();

        if (stored is not null && stored.OpportunityIds.Count > 0)
        {
            var batch = await HydrateBatchAsync(profile, stored);
            if (batch is not null) return batch;
        }

        return await BuildDailyFeedAsync(profile);
    }

    /// <summary>
    /// Rebuilds the full feed object from stored ids. Returns null if the stored batch no longer
    /// resolves — opportunities expire, so a stale batch must fall back to a fresh build rather
    /// than showing the user a short or empty day.
    /// </summary>
    private async Task<DailyOpportunityFeed?> HydrateBatchAsync(UserProfile profile, DailyOpportunityBatchDoc stored)
    {
        var items = await _db.Opportunities
            .Find(Builders<Opportunity>.Filter.In(o => o.Id, stored.OpportunityIds))
            .ToListAsync();

        if (items.Count == 0) return null;

        var trackingMap = await GetTrackingMapAsync(profile.Id);
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);
        foreach (var opp in items) EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord);

        var ordered = stored.OpportunityIds
            .Select(id => items.FirstOrDefault(i => i.Id == id))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();

        return new DailyOpportunityFeed
        {
            TotalCount = ordered.Count,
            Message = $"{ordered.Count} opportunities for you today",
            PrimaryFocus = activeRoadmap?.Interest ?? profile.PrimaryGoal,
            Items = ordered,
            NewToYouCount = stored.NewToYouCount,
            SeenEarlier = await GetSeenEarlierAsync(profile, stored.OpportunityIds),
            TierCounts = new Dictionary<string, int>
            {
                ["StrongMatch"] = ordered.Count(c => c.MatchTier == "StrongMatch"),
                ["Relevant"] = ordered.Count(c => c.MatchTier == "Relevant"),
                ["Explore"] = ordered.Count(c => c.MatchTier == "Explore")
            },
            UrgencyCounts = new Dictionary<string, int>
            {
                ["Urgent"] = ordered.Count(c => c.DaysUntilDeadline <= 7),
                ["TwoWeeks"] = ordered.Count(c => c.DaysUntilDeadline > 7 && c.DaysUntilDeadline <= 21),
                ["Open"] = ordered.Count(c => c.DaysUntilDeadline > 21)
            }
        };
    }

    /// <summary>
    /// Items shown on an earlier day that the user has not acted on, so a deadline cannot quietly
    /// pass just because the item stopped being new.
    /// </summary>
    private async Task<List<Opportunity>> GetSeenEarlierAsync(UserProfile profile, List<string> excludeIds)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var seenIds = (await _db.OpportunityImpressions.Find(d => d.UserId == profile.Id).ToListAsync())
            .Where(d => d.FirstShownOn != today && !excludeIds.Contains(d.OpportunityId))
            .Select(d => d.OpportunityId)
            .ToList();

        if (seenIds.Count == 0) return [];

        var now = DateTime.UtcNow;
        var items = await _db.Opportunities
            .Find(Builders<Opportunity>.Filter.In(o => o.Id, seenIds))
            .ToListAsync();

        var trackingMap = await GetTrackingMapAsync(profile.Id);
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);

        return items
            .Where(o => !o.Deadline.HasValue || o.Deadline.Value >= now)
            .Select(o => { EnrichOpportunityWithPersonalization(o, profile, activeRoadmap, trackingMap, learningRecord); return o; })
            .Where(o => o.UserTrackingStatus != OpportunityTrackingStatus.NotRelevant)
            .OrderBy(o => o.DaysUntilDeadline)
            .Take(20)
            .ToList();
    }

    /// <summary>Computes a fresh batch, stores it, and records what the user was shown.</summary>
    public async Task<DailyOpportunityFeed> BuildDailyFeedAsync(UserProfile profile)
    {
        await EnsureSeedAsync();

        var all = await GetScoringCandidatesAsync();
        var trackingMap = await GetTrackingMapAsync(profile.Id);
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);

        foreach (var opp in all)
        {
            EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord);
        }

        // Filter expired and NotRelevant
        var now = DateTime.UtcNow;
        var valid = all
            .Where(o => (!o.Deadline.HasValue || o.Deadline.Value >= now.AddDays(-1)) &&
                        o.UserTrackingStatus != OpportunityTrackingStatus.NotRelevant)
            .ToList();

        // What this user has already been shown on a previous day. Without this the "daily" batch is
        // just a deterministic ranking of a static set, so the same twelve items return every day —
        // which is exactly what users reported.
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var impressions = await _db.OpportunityImpressions
            .Find(d => d.UserId == profile.Id)
            .ToListAsync();

        var seenBefore = impressions
            .Where(d => d.FirstShownOn != today)
            .Select(d => d.OpportunityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var opp in valid)
            opp.IsNewToYou = !seenBefore.Contains(opp.Id);

        // Unseen first within every tier. An item the user has already passed over should not keep
        // taking a slot from something they have never been offered.
        var fresh = valid.Where(o => o.IsNewToYou).ToList();
        var pool = fresh.Count >= 12 ? fresh : valid;

        // Group into Tiers: Strong Match (80-100%), Relevant (60-79%), Explore (40-59%)
        var strongMatches = pool.Where(o => o.MatchScorePercent >= 80).OrderByDescending(o => o.MatchScorePercent).ToList();
        var relevantMatches = pool.Where(o => o.MatchScorePercent >= 60 && o.MatchScorePercent < 80).OrderByDescending(o => o.MatchScorePercent).ToList();
        var exploreMatches = pool.Where(o => o.MatchScorePercent < 60).OrderByDescending(o => o.MatchScorePercent).ToList();

        // Curate the target 12 items: ~6 Strong, ~4 Relevant, ~2 Explore
        var curated = new List<Opportunity>();
        curated.AddRange(strongMatches.Take(6));
        curated.AddRange(relevantMatches.Take(4));
        curated.AddRange(exploreMatches.Take(2));

        // If not enough, fill from remaining items — unseen ones first, then anything valid.
        if (curated.Count < 12)
        {
            var remaining = pool.Where(v => !curated.Any(c => c.Id == v.Id))
                .Concat(valid.Where(v => !curated.Any(c => c.Id == v.Id)))
                .DistinctBy(v => v.Id)
                .OrderByDescending(v => v.IsNewToYou)
                .ThenByDescending(v => v.MatchScorePercent)
                .Take(12 - curated.Count);
            curated.AddRange(remaining);
        }

        // Sort curated batch: Strong matches first, but prioritising urgent deadlines
        curated.Sort((a, b) =>
        {
            // Give urgent items (<= 7 days) a priority boost in ordering
            int aWeight = a.MatchScorePercent + (a.DaysUntilDeadline <= 7 ? 6 : 0);
            int bWeight = b.MatchScorePercent + (b.DaysUntilDeadline <= 7 ? 6 : 0);
            return bWeight.CompareTo(aWeight);
        });

        var feed = new DailyOpportunityFeed
        {
            TotalCount = curated.Count,
            Message = $"{curated.Count} new opportunities for you today",
            PrimaryFocus = activeRoadmap?.Interest ?? profile.PrimaryGoal,
            Items = curated,
            TierCounts = new Dictionary<string, int>
            {
                ["StrongMatch"] = curated.Count(c => c.MatchTier == "StrongMatch"),
                ["Relevant"] = curated.Count(c => c.MatchTier == "Relevant"),
                ["Explore"] = curated.Count(c => c.MatchTier == "Explore")
            },
            UrgencyCounts = new Dictionary<string, int>
            {
                ["Urgent"] = curated.Count(c => c.DaysUntilDeadline <= 7),
                ["TwoWeeks"] = curated.Count(c => c.DaysUntilDeadline > 7 && c.DaysUntilDeadline <= 21),
                ["Open"] = curated.Count(c => c.DaysUntilDeadline > 21)
            }
        };

        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        feed.NewToYouCount = curated.Count(c => c.IsNewToYou);
        feed.Message = feed.NewToYouCount > 0
            ? $"{feed.NewToYouCount} new opportunities for you today"
            : $"{curated.Count} opportunities for you today";
        feed.SeenEarlier = await GetSeenEarlierAsync(profile, curated.Select(c => c.Id).ToList());

        await _db.DailyOpportunityBatches.ReplaceOneAsync(
            d => d.Id == $"{profile.Id}:{todayKey}",
            new DailyOpportunityBatchDoc
            {
                Id = $"{profile.Id}:{todayKey}",
                UserId = profile.Id,
                Date = todayKey,
                OpportunityIds = curated.Select(c => c.Id).ToList(),
                NewToYouCount = feed.NewToYouCount,
                GeneratedAt = DateTime.UtcNow,
            },
            new ReplaceOptions { IsUpsert = true });

        await RecordImpressionsAsync(profile.Id, curated, todayKey);
        return feed;
    }

    /// <summary>
    /// Marks today's batch as shown. Upserted per user and opportunity so re-running the job is
    /// idempotent and cannot make an item look older than it is.
    /// </summary>
    private async Task RecordImpressionsAsync(string userId, List<Opportunity> items, string today)
    {
        foreach (var item in items)
        {
            var id = $"{userId}:{item.Id}";
            var existing = await _db.OpportunityImpressions.Find(d => d.Id == id).FirstOrDefaultAsync();

            await _db.OpportunityImpressions.ReplaceOneAsync(
                d => d.Id == id,
                new OpportunityImpressionDoc
                {
                    Id = id,
                    UserId = userId,
                    OpportunityId = item.Id,
                    FirstShownOn = existing?.FirstShownOn ?? today,
                    FirstShownAt = existing?.FirstShownAt ?? DateTime.UtcNow,
                    TimesShown = (existing?.TimesShown ?? 0) + 1,
                },
                new ReplaceOptions { IsUpsert = true });
        }
    }

    public async Task<Opportunity?> GetByIdAsync(string id, UserProfile? profile = null)
    {
        await EnsureSeedAsync();
        var opp = await _db.Opportunities.Find(o => o.Id == id).FirstOrDefaultAsync();
        if (opp == null) return null;

        if (profile != null)
        {
            var trackingMap = await GetTrackingMapAsync(profile.Id);
            var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
            var learningRecord = await RecordForAsync(profile);
            EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord);
        }

        return opp;
    }

    public async Task SaveOpportunityAsync(string userId, string opportunityId)
    {
        await UpdateStatusAsync(userId, opportunityId, OpportunityTrackingStatus.Saved);
    }

    public async Task UnsaveOpportunityAsync(string userId, string opportunityId)
    {
        await UpdateStatusAsync(userId, opportunityId, OpportunityTrackingStatus.None);
    }

    public async Task<List<Opportunity>> GetSavedOpportunitiesAsync(string userId)
    {
        await EnsureSeedAsync();
        var trackings = await _db.OpportunityTrackings
            .Find(t => t.UserId == userId && (t.Status == OpportunityTrackingStatus.Saved || t.Status == OpportunityTrackingStatus.Interested))
            .ToListAsync();

        var oppIds = trackings.Select(t => t.OpportunityId).ToList();
        if (oppIds.Count == 0) return [];

        var items = await _db.Opportunities.Find(Builders<Opportunity>.Filter.In(o => o.Id, oppIds)).ToListAsync();
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == userId && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);
        var trackingMap = trackings.ToDictionary(t => t.OpportunityId);

        foreach (var opp in items)
        {
            if (profile != null) EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord);
            opp.IsSaved = true;
        }

        return items;
    }

    public async Task MarkAppliedAsync(string userId, string opportunityId)
    {
        await UpdateStatusAsync(userId, opportunityId, OpportunityTrackingStatus.Applied);
    }

    public async Task UpdateStatusAsync(string userId, string opportunityId, OpportunityTrackingStatus status, string? notes = null)
    {
        var existing = await _db.OpportunityTrackings
            .Find(t => t.UserId == userId && t.OpportunityId == opportunityId)
            .FirstOrDefaultAsync();

        if (existing == null)
        {
            await _db.OpportunityTrackings.InsertOneAsync(new OpportunityTrackingDoc
            {
                UserId = userId,
                OpportunityId = opportunityId,
                Status = status,
                FeedbackNotes = notes,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Status = status;
            if (notes != null) existing.FeedbackNotes = notes;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.OpportunityTrackings.ReplaceOneAsync(t => t.Id == existing.Id, existing);
        }

        // Sync legacy collections for backward compatibility
        if (status == OpportunityTrackingStatus.Saved || status == OpportunityTrackingStatus.Interested)
        {
            var savedExists = await _db.SavedOpportunities.Find(s => s.UserId == userId && s.OpportunityId == opportunityId).AnyAsync();
            if (!savedExists)
                await _db.SavedOpportunities.InsertOneAsync(new SavedOpportunityDoc { UserId = userId, OpportunityId = opportunityId });
        }
        else if (status == OpportunityTrackingStatus.None || status == OpportunityTrackingStatus.NotRelevant)
        {
            await _db.SavedOpportunities.DeleteOneAsync(s => s.UserId == userId && s.OpportunityId == opportunityId);
        }

        if (status == OpportunityTrackingStatus.Applied)
        {
            var appliedExists = await _db.AppliedOpportunities.Find(a => a.UserId == userId && a.OpportunityId == opportunityId).AnyAsync();
            if (!appliedExists)
                await _db.AppliedOpportunities.InsertOneAsync(new AppliedOpportunityDoc { UserId = userId, OpportunityId = opportunityId });
        }

        await LearnFromTrackingAsync(userId, opportunityId, status);
    }

    /// <summary>
    /// Turns a tracking action into something Radar can generalise from.
    ///
    /// Marking one opportunity "not relevant" used to hide that single row and nothing more. The
    /// only generalisation was a flat -5 once a user had dismissed three of anything, under a
    /// variable called `notRelevantCountInType` that did not count by type - so dismissing three
    /// scholarships nudged jobs down by the same amount, and dismissing thirty taught no more than
    /// dismissing three.
    ///
    /// Recording the action against the opportunity's own terms feeds the same signal store the
    /// learning record is built from, so one dismissal now reaches the roadmap, the feed and
    /// future opportunity matching rather than one row.
    /// </summary>
    private async Task LearnFromTrackingAsync(string userId, string opportunityId, OpportunityTrackingStatus status)
    {
        var action = status switch
        {
            OpportunityTrackingStatus.Interested => BehavioralAction.MoreLikeThis,
            OpportunityTrackingStatus.Saved => BehavioralAction.Save,
            OpportunityTrackingStatus.Applied => BehavioralAction.Apply,
            OpportunityTrackingStatus.Accepted => BehavioralAction.Complete,
            OpportunityTrackingStatus.NotRelevant => BehavioralAction.NotRelevant,
            _ => (BehavioralAction?)null,
        };

        if (action is null) return;

        var opportunity = await _db.Opportunities.Find(o => o.Id == opportunityId).FirstOrDefaultAsync();
        if (opportunity is null) return;

        foreach (var term in OpportunitySignalTerms.For(opportunity))
        {
            // Declared, not Inferred. The user pressed a button saying what they think of this -
            // that is a statement, not a guess Radar made from their browsing, and confidence
            // scoring weights the two differently on purpose.
            await _signals.RecordAsync(userId, term, action.Value, SignalSource.Declared);
        }
    }

    public async Task ToggleReminderAsync(string userId, string opportunityId, bool reminderSet)
    {
        var existing = await _db.OpportunityTrackings
            .Find(t => t.UserId == userId && t.OpportunityId == opportunityId)
            .FirstOrDefaultAsync();

        if (existing == null)
        {
            await _db.OpportunityTrackings.InsertOneAsync(new OpportunityTrackingDoc
            {
                UserId = userId,
                OpportunityId = opportunityId,
                Status = OpportunityTrackingStatus.Saved,
                ReminderSet = reminderSet,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.ReminderSet = reminderSet;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.OpportunityTrackings.ReplaceOneAsync(t => t.Id == existing.Id, existing);
        }
    }

    public async Task<List<Opportunity>> GetTrackedPipelineAsync(string userId)
    {
        await EnsureSeedAsync();
        var trackings = await _db.OpportunityTrackings
            .Find(t => t.UserId == userId && t.Status != OpportunityTrackingStatus.None)
            .ToListAsync();

        var oppIds = trackings.Select(t => t.OpportunityId).ToList();
        if (oppIds.Count == 0) return [];

        var items = await _db.Opportunities.Find(Builders<Opportunity>.Filter.In(o => o.Id, oppIds)).ToListAsync();
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == userId && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);
        var trackingMap = trackings.ToDictionary(t => t.OpportunityId);

        foreach (var opp in items)
        {
            if (profile != null) EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord);
        }

        return items;
    }

    // ── Continuous Learning & Personalization ───────────────────────────────

    private void EnrichOpportunityWithPersonalization(
        Opportunity opp,
        UserProfile profile,
        GrowthRoadmap? activeRoadmap,
        Dictionary<string, OpportunityTrackingDoc> trackingMap,
        UserLearningRecord? record)
    {
        var signals = new List<MatchSignal>();
        int score = BaseScore;

        var fullText = $"{opp.Title} {opp.Description} {opp.Organisation} {opp.Benefits} {string.Join(" ", opp.Requirements)}".ToLowerInvariant();

        // What the learner is actually working on. The roadmap is the firmest statement of it, the
        // record is what their behaviour says, and the stated goal is the fallback. No field is
        // hardcoded here: an empty subject scores no domain points rather than quietly becoming
        // somebody else's field, which is how a nursing learner used to be matched on "security".
        var domain = Firm(activeRoadmap?.Interest) ?? Firm(record?.PrimaryGoal) ?? Firm(profile.PrimaryGoal);
        if (domain is not null && DomainVocabulary.MentionsSubject(fullText, domain))
        {
            score += 26;
            signals.Add(new MatchSignal(26, $"It is {SubjectPhrase.Lower(domain)} work, which is what you are learning."));
        }

        foreach (var interest in profile.Interests)
        {
            if (string.IsNullOrWhiteSpace(interest) || !fullText.Contains(interest.ToLowerInvariant())) continue;
            score += 8;
            signals.Add(new MatchSignal(8, $"You said you are interested in {SubjectPhrase.Lower(interest)}."));
        }

        // Where the user is. Remote is called out separately because it is the thing worth saying:
        // the role is open to them wherever they are.
        var userLoc = $"{profile.City} {profile.Region} {record?.Location}".ToLowerInvariant();
        var nearby = userLoc.Contains("nigeria") || userLoc.Contains("lagos") || userLoc.Contains("abuja") || userLoc.Contains("africa");
        if (nearby && (opp.RegionTag == "Nigeria" || opp.RegionTag == "Africa" || opp.IsRemote))
        {
            score += 10;
            signals.Add(new MatchSignal(10, opp.IsRemote
                ? "It is remote, so you can do it from where you are."
                : $"It is open to people in {opp.RegionTag}."));
        }

        var personaPoints = profile.Persona switch
        {
            // Graduate appeared in no scoring at all, so a graduate fell to the default and got
            // no persona matching whatsoever — while OpportunityType.GraduateProgramme, the
            // single most relevant type there is for one, matched nobody. Two gaps that only
            // existed because nothing was ever written for the persona in between studying and
            // working.
            PersonaType.Graduate when opp.Type is OpportunityType.GraduateProgramme
                or OpportunityType.Internship or OpportunityType.Fellowship
                or OpportunityType.Training or OpportunityType.Bootcamp => 10,
            PersonaType.Student when opp.Type is OpportunityType.Internship or OpportunityType.Scholarship or OpportunityType.Bootcamp => 8,
            PersonaType.YoungProfessional when opp.Type is OpportunityType.Job or OpportunityType.Fellowship or OpportunityType.RemoteJob => 8,
            PersonaType.Entrepreneur when opp.Type is OpportunityType.StartupAccelerator or OpportunityType.ResearchGrant or OpportunityType.Funding => 10,
            PersonaType.Researcher when opp.Type is OpportunityType.ResearchGrant or OpportunityType.Fellowship or OpportunityType.Conference => 10,
            _ => 4
        };
        score += personaPoints;
        if (personaPoints > 4)
        {
            signals.Add(new MatchSignal(personaPoints, "It is the kind of opening that suits where you are now."));
        }

        var days = opp.DaysUntilDeadline;
        var deadlinePoints = days switch { <= 0 => 0, <= 7 => 6, <= 21 => 3, _ => 0 };
        if (deadlinePoints > 0)
        {
            score += deadlinePoints;
            signals.Add(new MatchSignal(deadlinePoints, days == 1
                ? "The deadline is tomorrow."
                : $"The deadline is in {days} days."));
        }

        // What the record knows that the profile does not. Gated on confidence for the same reason
        // the roadmap personaliser is: acting on two lessons' worth of evidence tells the user
        // something about themselves that is not yet true.
        if (record is not null && record.Confidence >= MinConfidenceToUseRecord)
        {
            var role = Firm(record.TargetRole);
            if (role is not null && fullText.Contains(role.ToLowerInvariant()))
            {
                score += 12;
                signals.Add(new MatchSignal(12, $"It is a step toward {SubjectPhrase.Lower(role)}, the role you are aiming for."));
            }

            var priority = record.PriorityTopics.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && fullText.Contains(t.ToLowerInvariant()));
            if (priority is not null)
            {
                score += 10;
                signals.Add(new MatchSignal(10, $"It covers {SubjectPhrase.Lower(priority)}, which is what you are learning next."));
            }

            var weak = record.WeakAreas.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w) && fullText.Contains(w.ToLowerInvariant()));
            if (weak is not null)
            {
                score += 6;
                signals.Add(new MatchSignal(6, $"It gives you practice in {SubjectPhrase.Lower(weak)}, which you are still building."));
            }
        }

        if (trackingMap.TryGetValue(opp.Id, out var tracking))
        {
            opp.UserTrackingStatus = tracking.Status;
            opp.ReminderSet = tracking.ReminderSet;
            opp.IsSaved = tracking.Status == OpportunityTrackingStatus.Saved || tracking.Status == OpportunityTrackingStatus.Interested;

            switch (tracking.Status)
            {
                case OpportunityTrackingStatus.Interested:
                    score += 8;
                    signals.Add(new MatchSignal(8, "You marked this one as interesting."));
                    break;
                case OpportunityTrackingStatus.Applied:
                    score += 6;
                    signals.Add(new MatchSignal(6, "You have already applied to this."));
                    break;
                case OpportunityTrackingStatus.Accepted:
                    score += 15;
                    signals.Add(new MatchSignal(15, "You were accepted to this."));
                    break;
                case OpportunityTrackingStatus.NotRelevant:
                    score -= 35;
                    break;
            }
        }

        // A blunt backstop, kept small on purpose. It counts dismissals of anything at all, so it
        // cannot tell a user who is done with scholarships from one who is done with Radar - and
        // it was previously read through a variable named `notRelevantCountInType`, which counted
        // no such thing. The specific work is done by the signals recorded in
        // LearnFromTrackingAsync, which generalise by type, goal and field rather than by volume.
        if (trackingMap.Values.Count(t => t.Status == OpportunityTrackingStatus.NotRelevant) > 2) score -= 5;

        opp.MatchScorePercent = Math.Clamp(score, 38, 98);
        opp.WhyItFits = ExplainMatch(signals);

        if (opp.NextMoveSteps.Count == 0)
        {
            opp.NextMoveSteps =
            [
                "Check you meet the requirements",
                "Tailor your CV with Radar's Learning Mentor",
                "Write your personal statement",
                "Apply, then record it in your tracker"
            ];
        }
    }

    /// <summary>A reason the score moved, written the way the user will read it.</summary>
    private sealed record MatchSignal(int Points, string Reason);

    /// <summary>Where everyone starts, before anything is known about the fit.</summary>
    private const int BaseScore = 48;

    /// <summary>
    /// How sure the record has to be before it is allowed to move a match. The same threshold the
    /// roadmap personaliser uses, because it is the same judgement: thin evidence should not be
    /// shown to the user as a conclusion about them.
    /// </summary>
    private const double MinConfidenceToUseRecord = 0.35;

    private static string? Firm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The match explained by the things that actually moved its score.
    ///
    /// This used to be three paragraphs chosen by tier, so every strong match claimed it "aligns
    /// with your current active stage, target roles" whether or not anything had been checked, and
    /// every weak one was a "serendipitous discovery broadening your cross-disciplinary horizon".
    /// Deriving the sentence from the signals makes it impossible to claim a reason the scoring did
    /// not find, and when nothing matched, saying so plainly beats inventing a connection.
    /// </summary>
    private static string ExplainMatch(List<MatchSignal> signals)
    {
        var top = signals.OrderByDescending(s => s.Points).Take(3).Select(s => s.Reason).ToList();
        if (top.Count == 0)
        {
            return "This one does not line up with your goal yet. It is here to widen what you see.";
        }

        return string.Join(" ", top);
    }

    private async Task<Dictionary<string, OpportunityTrackingDoc>> GetTrackingMapAsync(string userId)
    {
        var docs = await _db.OpportunityTrackings.Find(t => t.UserId == userId).ToListAsync();
        return docs.ToDictionary(t => t.OpportunityId, StringComparer.OrdinalIgnoreCase);
    }

    // ── Extensive Curated Seed Catalog ───────────────────────────────────────

    private async Task EnsureSeedAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            var count = await _db.Opportunities.CountDocumentsAsync(Builders<Opportunity>.Filter.Empty);
            if (count < 15)
            {
                // Clear old sparse seeds and insert the rich Opportunity Intelligence catalog
                await _db.Opportunities.DeleteManyAsync(Builders<Opportunity>.Filter.Empty);
                await _db.Opportunities.InsertManyAsync(SeedData);
            }
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static readonly List<Opportunity> SeedData =
    [
        // ── CYBERSECURITY & SYSTEMS ─────────────────────────────────────────
        new()
        {
            Id = "opp-google-cyber-fellow",
            Type = OpportunityType.Fellowship,
            Title = "Google Cybersecurity Professional Fellowship",
            Organisation = "Google Security Team & Africa Digital Hub",
            Description = "A 6-month intensive fellowship providing hands-on training with Google's Security Operations team. Fellows receive direct mentorship from Principal Security Engineers, participate in live incident response simulations, and receive a fully funded Google Security Professional Credential.",
            Deadline = DateTime.UtcNow.AddDays(14),
            Location = "Lagos, Nigeria / Remote Pan-Africa",
            RegionTag = "Nigeria",
            GoalTag = "Career",
            IsRemote = true,
            Benefits = "USD 2,500/month stipend, Google Cloud & Security lab credits, direct fast-track interview for Associate Security Engineer",
            Cost = "Free / Fully Funded",
            Url = "https://grow.google/certificates/cybersecurity/",
            Requirements = ["Foundational networking & Linux knowledge", "Demonstrated commitment to cybersecurity or software engineering", "Resident in an African country"],
            WhoIsEligible = ["Final year undergraduates, recent graduates, or young professionals transitioning into cybersecurity"],
            RadarsAdvice =
            [
                "Apply before the final week; candidates with verified GitHub SOC/Wireshark labs get reviewed first.",
                "Emphasise your understanding of packet analysis and incident response workflows in your statement.",
                "Highlight any CTF (Capture The Flag) competitions or practical exercises you have completed on your Radar roadmap.",
                "Review Google's public Zero Trust framework before the technical screening interview."
            ],
            NextMoveSteps =
            [
                "Verify eligibility and ensure your CV highlights network security tools",
                "Draft a 400-word statement explaining how you will apply cybersecurity in African enterprise",
                "Ask Radar's AI Mentor to review your application responses",
                "Submit official application via Google Career portal"
            ],
            SelectionProcess = "Application Review → Technical Aptitude Challenge → 30-min Technical Behavioral Interview"
        },
        new()
        {
            Id = "opp-sans-cyber-academy",
            Type = OpportunityType.Scholarship,
            Title = "SANS Cyber Immersion Academy Scholarship",
            Organisation = "SANS Institute",
            Description = "100% scholarship covering GIAC Foundational Cybersecurity (GFACT) and GIAC Security Essentials (GSEC) certifications. Includes live virtual instruction, hands-on enterprise cyber ranges, and access to SANS career matching network.",
            Deadline = DateTime.UtcNow.AddDays(4),
            Location = "Global / Virtual Range",
            RegionTag = "Global",
            GoalTag = "Skills",
            IsRemote = true,
            Benefits = "Over $12,000 worth of GIAC courseware and exam vouchers, private industry hiring showcase",
            Cost = "Free / Fully Funded",
            Url = "https://www.sans.org/scholarship-academies/",
            Requirements = ["Must achieve passing score on SANS Cyber Aptitude Assessment", "Able to commit 15 hours/week for 4 months", "No prior commercial security certifications required"],
            WhoIsEligible = ["Motivated individuals with high problem-solving aptitude seeking rapid entry into defensive security"],
            RadarsAdvice =
            [
                "URGENT: Only 4 days remaining before the cohort closes.",
                "Take the aptitude practice test in a quiet environment — it tests logic, pattern recognition, and basic binary thinking.",
                "Do not spend time cramming terminology; focus on logical deduction and step-by-step troubleshooting.",
                "Mention your progress on the Radar Cybersecurity Learning Path to show dedication."
            ],
            NextMoveSteps =
            [
                "Complete the free 45-minute Cyber Aptitude Assessment immediately",
                "Upload your CV emphasizing analytical troubleshooting and problem solving",
                "Submit before Sunday midnight"
            ],
            SelectionProcess = "Cyber Aptitude Assessment → Video Introduction → Formal Scholarship Award"
        },
        new()
        {
            Id = "opp-cloudflare-sec-intern",
            Type = OpportunityType.Internship,
            Title = "Systems & Security Engineering Internship",
            Organisation = "Cloudflare",
            Description = "Join Cloudflare's security engineering division building global DDoS mitigation, TLS termination, and firewall edge engines. Work with production Rust and Go services handling 55+ million HTTP requests per second.",
            Deadline = DateTime.UtcNow.AddDays(25),
            Location = "Remote — EMEA / London",
            RegionTag = "Remote",
            GoalTag = "Career",
            IsRemote = true,
            Benefits = "£4,200/month competitive internship compensation, equipment stipend, mentorship from Internet infrastructure pioneers",
            Cost = "Free Application",
            Url = "https://www.cloudflare.com/careers/internships/",
            Requirements = ["Solid understanding of TCP/IP, DNS, and HTTP/HTTPS protocols", "Proficiency in Rust, Go, Python, or modern C++", "Curiosity about Internet protocols and edge architecture"],
            WhoIsEligible = ["Students graduating within the next 18 months or self-taught developers with strong open-source systems contributions"],
            RadarsAdvice =
            [
                "Cloudflare values people who read RFCs and understand how packet headers work under the hood.",
                "Link a GitHub repository demonstrating low-level networking, packet parsing, or an HTTP proxy project.",
                "Be prepared to explain what happens when you type 'cloudflare.com' into a browser and press Enter.",
                "Use the AI Learning Mentor to practice Systems & Protocol interview questions."
            ],
            NextMoveSteps =
            [
                "Polish your GitHub code samples (clean README, tests, architecture diagrams)",
                "Prepare a tailored 1-page resume focusing on systems and protocols",
                "Submit official application online"
            ],
            SelectionProcess = "Resume Screening → Take-Home Networking Coding Challenge → Systems Architecture Interview"
        },
        new()
        {
            Id = "opp-nitda-cyber-fund",
            Type = OpportunityType.ResearchGrant,
            Title = "NITDA National Cybersecurity Innovation Grant",
            Organisation = "National Information Technology Development Agency (NITDA)",
            Description = "Seed grants of up to ₦15,000,000 for innovative cybersecurity startups and research projects addressing threat intelligence, fraud detection, and digital identity defense across Nigerian financial and governmental infrastructure.",
            Deadline = DateTime.UtcNow.AddDays(18),
            Location = "Abuja & Lagos, Nigeria",
            RegionTag = "Nigeria",
            GoalTag = "Funding",
            IsRemote = true,
            Benefits = "₦15,000,000 equity-free grant, regulatory sandbox support, access to national infrastructure datasets",
            Cost = "Free Application",
            Url = "https://nitda.gov.ng/",
            Requirements = ["Nigerian citizen or locally registered startup entity", "Working prototype or technical proof-of-concept", "Clear applicability to Nigerian cyber threat landscape"],
            WhoIsEligible = ["Security researchers, technical founders, and academic teams building defensive cybersecurity products"],
            RadarsAdvice =
            [
                "Frame your proposal around addressing high-impact domestic threats: USSD banking fraud, SIM swap detection, or ransomware in healthcare.",
                "Provide an architecture diagram showing data flow and latency impact.",
                "Emphasise local economic benefit and job creation in your pitch deck.",
                "Ask Radar's AI Mentor to critique your technical grant proposal."
            ],
            NextMoveSteps =
            [
                "Download the official NITDA project submission template",
                "Draft technical abstract and cost breakdown for equipment and cloud hosting",
                "Submit your grant proposal before the 18-day cutoff"
            ],
            SelectionProcess = "Technical Feasibility Screening → Pitch Session with National Innovation Panel → Grant Disbursement"
        },
        new()
        {
            Id = "opp-hackerone-bounty",
            Type = OpportunityType.Competition,
            Title = "HackerOne Africa Cyber Defense CTF & Bug Bounty Challenge",
            Organisation = "HackerOne & Cyber Africa Alliance",
            Description = "A virtual Capture The Flag (CTF) tournament followed by a private live bug bounty event on African fintech targets. Discover valid vulnerabilities in real-world API endpoints, earn verified CVE credits and cash bounties.",
            Deadline = DateTime.UtcNow.AddDays(7),
            Location = "Virtual / Lagos Hub",
            RegionTag = "Africa",
            GoalTag = "Skills",
            IsRemote = true,
            Benefits = "USD 25,000 total bounty pool, private invites to top tier enterprise bug bounty programs, HackerOne leaderboard ranking",
            Cost = "Free to participate",
            Url = "https://www.hackerone.com/events",
            Requirements = ["Strict adherence to HackerOne Code of Conduct and Scope of Engagement", "Basic understanding of OWASP Top 10 web vulnerabilities", "Burp Suite or ZAP proxy familiarity"],
            WhoIsEligible = ["Ethical hackers, security students, and software developers across Africa"],
            RadarsAdvice =
            [
                "Focus on IDOR (Insecure Direct Object Reference) and authorization logic flaws; these are the most common high-payout bugs.",
                "Write clear, reproducible Proof-of-Concept (PoC) reports with step-by-step remediation suggestions.",
                "Review the PortSwigger Web Security Academy topics on your Radar Learn Hub before the kickoff.",
                "Team up with another researcher to divide testing between front-end logic and backend APIs."
            ],
            NextMoveSteps =
            [
                "Register your HackerOne profile and link your 2FA authentication",
                "Spend 2 hours reviewing Burp Suite HTTP history and Repeater workflows",
                "Join the official Discord channel before kickoff"
            ],
            SelectionProcess = "Open Virtual Qualifier → Live Bounty Window → Vulnerability Triage and Payout"
        },

        // ── NIGERIA & AFRICA TECH & STRATEGY ────────────────────────────────
        new()
        {
            Id = "opp-paystack-sec-engineer",
            Type = OpportunityType.Job,
            Title = "Junior Security Operations & Infrastructure Engineer",
            Organisation = "Paystack (Stripe Company)",
            Description = "Help protect billions of dollars in transaction flows across Africa. You will monitor AWS infrastructure, manage Wazuh/SIEM detection pipelines, conduct vulnerability scans, and assist with PCI-DSS compliance audits.",
            Deadline = DateTime.UtcNow.AddDays(12),
            Location = "Lagos, Nigeria (Hybrid / Remote)",
            RegionTag = "Nigeria",
            GoalTag = "Career",
            IsRemote = true,
            Benefits = "Competitive USD-benchmarked salary, comprehensive health insurance, $2,000 annual learning stipend, Stripe equity stock options",
            Cost = "Free Application",
            Url = "https://paystack.com/careers",
            Requirements = ["Experience with AWS or Linux server administration", "Understanding of SIEM logs and network protocols", "Familiarity with Python or Bash scripting", "High integrity and attention to detail"],
            WhoIsEligible = ["Engineers with 0–2 years of experience or strong practical cybersecurity lab portfolios"],
            RadarsAdvice =
            [
                "Paystack cares deeply about practical demonstration: showcase your hands-on pfSense/Wazuh home lab from your Radar Learn Hub.",
                "Read the Stripe Engineering blog articles on cross-border payments security.",
                "Demonstrate clear written communication — write a concise cover note explaining your security journey.",
                "Use the AI Learning Mentor to practice PCI-DSS and Cloud Security interview scenarios."
            ],
            NextMoveSteps =
            [
                "Review the Paystack career spec and update your LinkedIn profile",
                "Write a 1-page technical writeup of a security lab you built",
                "Submit your direct application via Paystack's Greenhouse portal"
            ],
            SelectionProcess = "Resume Screen → Take-home Security Scenario Assessment → Engineering Team Interview"
        },
        new()
        {
            Id = "opp-tony-elumelu",
            Type = OpportunityType.Funding,
            Title = "Tony Elumelu Foundation Entrepreneurship Program",
            Organisation = "Tony Elumelu Foundation",
            Description = "Africa's leading entrepreneurship initiative providing $5,000 non-refundable seed capital, a 12-week intensive business management training curriculum, and world-class mentorship for 1,000 African entrepreneurs every year.",
            Deadline = DateTime.UtcNow.AddDays(28),
            Location = "Pan-African (Online Training + National Hubs)",
            RegionTag = "Africa",
            GoalTag = "Funding",
            IsRemote = true,
            Benefits = "USD 5,000 non-refundable seed capital, TEFConnect business alumni network, access to commercial bank loan syndicates",
            Cost = "Free Application",
            Url = "https://www.tonyelumelufoundation.org/tef-entrepreneurship-programme",
            Requirements = ["Must be a citizen and resident of an African country", "Business idea or existing company between 0 and 3 years old", "Scalable business model with measurable community impact"],
            WhoIsEligible = ["Early-stage African founders in tech, agribusiness, commerce, manufacturing, and healthcare"],
            RadarsAdvice =
            [
                "Emphasise your path to revenue and clear unit economics rather than theoretical market size.",
                "Clearly describe the exact problem in your local community that your solution solves.",
                "Record a crisp, professional 90-second elevator pitch video answering: Problem, Solution, Team, and Use of $5,000.",
                "Complete the application early to avoid last-minute portal slowdowns."
            ],
            NextMoveSteps =
            [
                "Create your profile on the TEFConnect portal",
                "Complete the business diagnostic questionnaire",
                "Submit business plan summary and verification documents"
            ],
            SelectionProcess = "Aptitude Assessment → 12-Week Business Training → Pitch Presentation → Seed Capital Award"
        },
        new()
        {
            Id = "opp-mastercard-scholars",
            Type = OpportunityType.Scholarship,
            Title = "MasterCard Foundation Scholars Program",
            Organisation = "MasterCard Foundation",
            Description = "Full comprehensive scholarship for academically gifted young Africans facing financial barriers to pursue undergraduate and postgraduate degrees at leading universities worldwide (UCT, Oxford, Edinburgh, CMU Africa). Covers 100% tuition, campus accommodation, books, living stipend, and return international travel.",
            Deadline = DateTime.UtcNow.AddMonths(2),
            Location = "Pan-Africa & Global Partner Universities",
            RegionTag = "Africa",
            GoalTag = "Education",
            IsRemote = false,
            Benefits = "Full tuition, living stipend, laptop, health insurance, global leadership development program",
            Cost = "Free / Fully Funded",
            Url = "https://mastercardfdn.org/all/scholars/",
            Requirements = ["Citizen of an African country", "Demonstrated commitment to returning and serving community", "Strong academic record (top 15% of class)", "Demonstrated financial need"],
            WhoIsEligible = ["Prospective undergraduate and master's students with proven leadership and civic engagement"],
            RadarsAdvice =
            [
                "Focus on your leadership ethos: MasterCard evaluates 'transformative leadership' — how have you used limited resources to help others?",
                "Secure two strong academic recommendation letters that speak to your character and resilience.",
                "Link your study program directly to an economic challenge in your home country.",
                "Apply to the partner university first or simultaneously as specified by each institution."
            ],
            NextMoveSteps =
            [
                "Select 2 partner universities aligned with your degree goal",
                "Prepare personal statement reflecting leadership and financial background",
                "Gather certified academic transcripts and recommendation letters"
            ],
            SelectionProcess = "University Admission → MasterCard Foundation Committee Review → Final Scholar Selection"
        },
        new()
        {
            Id = "opp-lagos-tech-fest",
            Type = OpportunityType.Conference,
            Title = "Lagos Tech Fest & Venture Showcase 2026",
            Organisation = "Techpoint Africa & Lagos State Ministry of Innovation",
            Description = "West Africa's largest gathering of tech founders, corporate innovators, venture capitalists, and engineers. Features high-impact panel discussions on African sovereign AI, fintech regulation, and cybersecurity defense, alongside a dedicated career exhibition.",
            Deadline = DateTime.UtcNow.AddDays(10),
            Location = "Landmark Centre, Victoria Island, Lagos",
            RegionTag = "Nigeria",
            GoalTag = "Networking",
            IsRemote = false,
            Benefits = "Direct access to 200+ tech hiring managers, fast-pitch venture competitions, complimentary 1-year developer tool perks",
            Cost = "Free Student & Developer Passes (Limited)",
            Url = "https://techpoint.africa/events",
            Requirements = ["Registration via official portal", "Demonstrated involvement in technology, product, or startup ecosystem"],
            WhoIsEligible = ["Software developers, analysts, founders, university STEM students, and career switchers"],
            RadarsAdvice =
            [
                "Free student/developer passes run out quickly — claim yours today.",
                "Prepare a concise 30-second introduction: your name, your primary skill (e.g. Cybersecurity/Data), and what you are building.",
                "Download the attendee app early to book 1-on-1 networking slots with startup engineering leads.",
                "Have your GitHub and LinkedIn QR codes ready on your phone for frictionless connection."
            ],
            NextMoveSteps =
            [
                "Claim your verified developer ticket online",
                "Review the session schedule and mark key cybersecurity and career workshops",
                "Connect with speakers on LinkedIn ahead of time"
            ],
            SelectionProcess = "Direct Registration → Badge Confirmation Email"
        },

        // ── DATA ANALYTICS & ARTIFICIAL INTELLIGENCE ────────────────────────
        new()
        {
            Id = "opp-deepmind-africa-scholar",
            Type = OpportunityType.Scholarship,
            Title = "DeepMind Africa AI & Machine Learning Scholarship",
            Organisation = "Google DeepMind",
            Description = "Fully funded postgraduate scholarship supporting exceptional African scholars pursuing master's degrees in Artificial Intelligence, Machine Learning, and Computer Science at partner African institutions (including AIMS and Wits).",
            Deadline = DateTime.UtcNow.AddDays(40),
            Location = "AIMS Rwanda / South Africa / Virtual",
            RegionTag = "Africa",
            GoalTag = "Education",
            IsRemote = false,
            Benefits = "Full university tuition fees, living stipend, travel expenses, attendance at DeepMind international conference, dedicated DeepMind researcher mentor",
            Cost = "Free / Fully Funded",
            Url = "https://www.deepmind.com/scholarships",
            Requirements = ["African nationality and residency", "Strong mathematics and programming background (Python/PyTorch)", "Offer of admission to an approved partner institution for master's degree"],
            WhoIsEligible = ["Outstanding students from underrepresented backgrounds in machine learning"],
            RadarsAdvice =
            [
                "DeepMind looks for solid mathematical intuition: linear algebra, calculus, and probability.",
                "Highlight any projects involving neural network training or fine-tuning on open-source datasets.",
                "Articulate your research ambition clearly: what problem in healthcare, climate, or language modeling do you want to tackle?",
                "Work with Radar's AI Mentor to draft and polish your research statement."
            ],
            NextMoveSteps =
            [
                "Apply to an approved partner master's program (e.g. AIMS or Wits)",
                "Prepare personal statement describing your research interests in AI",
                "Submit DeepMind scholarship application form"
            ],
            SelectionProcess = "Academic Assessment → Research Proposal Review → Interview with DeepMind Panel"
        },
        new()
        {
            Id = "opp-stripe-emea-analyst",
            Type = OpportunityType.RemoteJob,
            Title = "Junior Data & Risk Analyst — EMEA Remote",
            Organisation = "Stripe",
            Description = "Analyze payment anomalies, fraud vectors, and transaction reconciliation flows across Stripe's EMEA payment rails. Build real-time SQL queries, develop dashboards in Looker/dbt, and collaborate with engineering on automated fraud mitigation rules.",
            Deadline = DateTime.UtcNow.AddDays(19),
            Location = "Remote — EMEA (Flexible African Timezones)",
            RegionTag = "Remote",
            GoalTag = "Career",
            IsRemote = true,
            Benefits = "USD 65,000–80,000 annual salary, Stripe equity grants, home office budget, flexible wellness benefits",
            Cost = "Free Application",
            Url = "https://stripe.com/jobs",
            Requirements = ["Proficiency in SQL (window functions, CTEs) and Python (Pandas)", "Experience analyzing large datasets and translating numbers into business impact", "Strong problem-solving orientation and curiosity about financial systems"],
            WhoIsEligible = ["Data analysts with 1–3 years experience or strong demonstrated analytics portfolios"],
            RadarsAdvice =
            [
                "Stripe tests SQL rigorously: expect live coding with window functions, joins, and reconciliation logic.",
                "Review the Stripe Engineering blog post 'What actually breaks when you scale payments across borders' on your Radar Feed.",
                "Be ready to discuss trade-offs between precision and recall in automated fraud detection.",
                "Use the AI Learning Mentor to practice live SQL interview questions."
            ],
            NextMoveSteps =
            [
                "Complete a sample SQL cohort analysis in your portfolio",
                "Tailor your CV to highlight measurable quantitative impact",
                "Apply through Stripe's verified career portal"
            ],
            SelectionProcess = "Recruiter Screen → Technical SQL Assessment → Take-Home Case Study → Executive Interview"
        },

        // ── GLOBAL PRESTIGE & LEADERSHIP ────────────────────────────────────
        new()
        {
            Id = "opp-chevening-uk",
            Type = OpportunityType.Scholarship,
            Title = "Chevening UK Government Postgraduate Scholarship",
            Organisation = "UK Foreign, Commonwealth & Development Office",
            Description = "The UK government's global scholarship programme, funded by the FCDO. Offers full financial support to undertake any eligible 1-year master's degree at any UK university, giving future leaders the opportunity to develop professionally and academically while networking extensively in the UK.",
            Deadline = DateTime.UtcNow.AddDays(35),
            Location = "United Kingdom (All Universities)",
            RegionTag = "Global",
            GoalTag = "Education",
            IsRemote = false,
            Benefits = "Full university tuition fees, monthly living allowance, return economy flights, arrival allowance, visa grant",
            Cost = "Free / Fully Funded",
            Url = "https://www.chevening.org/apply/",
            Requirements = ["Citizen of a Chevening-eligible country (Nigeria, Ghana, Kenya, etc.)", "At least two years of work experience (equivalent to 2,800 hours)", "Undergraduate degree of at least upper second-class (2:1)", "Commitment to return to home country for minimum 2 years post-graduation"],
            WhoIsEligible = ["Emerging leaders with strong career trajectory and clear strategic vision for their country"],
            RadarsAdvice =
            [
                "Chevening is won on the four essays: Leadership, Networking, Studying in the UK, and Career Plan.",
                "Use the STAR technique (Situation, Task, Action, Result) in every essay — quantify your concrete individual contribution.",
                "Choose three university courses that are logically interconnected with your 5-year career roadmap.",
                "Have two mentors review your essays for clarity and persuasive conviction."
            ],
            NextMoveSteps =
            [
                "Draft your 4 Chevening essays in Radar's Notebook",
                "Select three UK master's programs and initiate university applications",
                "Contact 2 professional referees for letters of support"
            ],
            SelectionProcess = "Online Application → Independent Reading Committee Review → Embassy Interview in Lagos/Abuja"
        },
        new()
        {
            Id = "opp-ycombinator-batch",
            Type = OpportunityType.StartupAccelerator,
            Title = "Y Combinator Startup Accelerator Batch",
            Organisation = "Y Combinator",
            Description = "The world's premier startup accelerator. YC invests $500,000 into early-stage companies across two cohorts per year. Founders spend 3 months in San Francisco working intensively on product-market fit, followed by global Demo Day.",
            Deadline = DateTime.UtcNow.AddDays(50),
            Location = "San Francisco, CA / Global",
            RegionTag = "Global",
            GoalTag = "Business",
            IsRemote = false,
            Benefits = "$500,000 standard investment deal, unparalleled network of 10,000+ alumni founders, weekly private partner office hours",
            Cost = "Equity investment",
            Url = "https://www.ycombinator.com/apply",
            Requirements = ["Technical co-founder or high technical execution velocity", "Demonstrated prototype or measurable customer engagement", "Clear understanding of the market and target user"],
            WhoIsEligible = ["Early-stage software and hardware founders globally at idea, prototype, or seed stage"],
            RadarsAdvice =
            [
                "Be concise: YC partners read applications in 2 minutes. State clearly what you make, who needs it, and why now.",
                "Quantify traction: users, waitlist count, revenue, or weekly growth percentage.",
                "Make a 1-minute unedited founder video introducing the team and why you are suited to solve this problem.",
                "Ask Radar's AI Mentor to evaluate your YC application answers."
            ],
            NextMoveSteps =
            [
                "Fill out the YC application questions clearly and without buzzwords",
                "Record a 1-minute informal founder video on your smartphone",
                "Submit your application early for rolling review"
            ],
            SelectionProcess = "Written Application Review → 10-Minute Video Interview with YC Partners → Same-day Decision"
        }
    ];
}

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

        // Newest first, which the window depends on and did not have.
        //
        // Unsorted, Mongo returns whatever order it stores, and in practice that is insertion
        // order - so the five hundred places filled with the oldest rows and every opportunity
        // ingested after them was never scored and never shown. One source arriving with several
        // hundred entries was enough to freeze the window permanently, because most job postings
        // carry no deadline and so never expire out of it. "New opportunities every day" stops
        // being true, silently, which is the failure the ingestion was built to end.
        return await _db.Opportunities
            .Find(notExpired)
            .SortByDescending(o => o.DiscoveredAt)
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
        var all = await GetScoringCandidatesAsync();

        // Reconcile user tracking & continuous learning weights
        var trackingMap = await GetTrackingMapAsync(profile.Id);
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);

        var aboutThisLearner = new HashSet<string>(StringComparer.Ordinal);

        foreach (var opp in all)
        {
            if (EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord))
            {
                aboutThisLearner.Add(opp.Id);
            }
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

        // Only what Radar can say something about.
        //
        // Every opportunity started at 48 points with a floor of 38, so one matching nothing
        // about this learner still came back at about half and sat in the Explore tier. Asked for
        // opportunities, somebody studying nursing was shown accelerator programmes because the
        // list was long enough to fill a page. An opportunity nobody can explain the presence of
        // is not a weak match - it is noise with a percentage next to it.
        //
        // A learner who has stated nothing gets nothing here, and the screen says to personalise.
        // That is the same rule as the Intelligence feed, and it is the honest one: Radar asks
        // what somebody wants rather than deriving it.
        valid = valid.Where(o => aboutThisLearner.Contains(o.Id)).ToList();

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
        // Re-checked rather than taken on trust. A batch stored this morning was matched against
        // what the learner had stated then, and somebody who answers the personalise questions at
        // lunchtime should not spend the afternoon with the feed they would have had anyway.
        //
        // And anything they dismissed is gone. This was the one read of the four with no
        // NotRelevant filter: the other three have it, so marking an item not relevant removed it
        // from the All view and it came back on the daily feed at the next refetch, scoring 38 per
        // cent. The two views disagreed about the same dismissal.
        var stillRelevant = items
            .Where(opp =>
                EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord)
                && opp.UserTrackingStatus != OpportunityTrackingStatus.NotRelevant)
            .ToList();

        var ordered = stored.OpportunityIds
            .Select(id => stillRelevant.FirstOrDefault(i => i.Id == id))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();

        // A batch most of which no longer applies is rebuilt, not served short.
        //
        // The fallback used to need the batch to be entirely gone, while the comment above it
        // promised no "short or empty day". Twelve stored in the morning, nine of them stopped
        // matching at lunchtime, and the afternoon was three items with no top-up. Rebuilding
        // costs one scan and gives the learner the day they would have had if they had asked an
        // hour later.
        if (ordered.Count < MinimumHydratedBatch) return null;

        return new DailyOpportunityFeed
        {
            TotalCount = ordered.Count,
            Message = $"{ordered.Count} opportunities for you today",
            PrimaryFocus = activeRoadmap?.Interest ?? profile.PrimaryGoal,
            Items = ordered,
            // Counted from what is actually being shown. Copied from the morning's stored value,
            // this could claim more new items than the batch now has in it.
            NewToYouCount = ordered.Count(o => o.IsNewToYou),
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

        // Scored and filtered on the same terms as the rest. Shown on an earlier day is not a
        // reason to keep showing something the learner no longer matches: somebody whose
        // interests moved from cybersecurity to nursing was carrying last week's security rows
        // under a nursing feed.
        return items
            .Where(o => !o.Deadline.HasValue || o.Deadline.Value >= now)
            .Where(o => EnrichOpportunityWithPersonalization(o, profile, activeRoadmap, trackingMap, learningRecord))
            .Where(o => o.UserTrackingStatus != OpportunityTrackingStatus.NotRelevant)
            .OrderBy(o => o.DaysUntilDeadline)
            .Take(20)
            .ToList();
    }

    /// <summary>Computes a fresh batch, stores it, and records what the user was shown.</summary>
    public async Task<DailyOpportunityFeed> BuildDailyFeedAsync(UserProfile profile)
    {
        var all = await GetScoringCandidatesAsync();
        var trackingMap = await GetTrackingMapAsync(profile.Id);
        var activeRoadmap = await _db.Roadmaps.Find(r => r.UserId == profile.Id && r.IsActive).FirstOrDefaultAsync();
        var learningRecord = await RecordForAsync(profile);

        var aboutThisLearner = new HashSet<string>(StringComparer.Ordinal);

        foreach (var opp in all)
        {
            if (EnrichOpportunityWithPersonalization(opp, profile, activeRoadmap, trackingMap, learningRecord))
            {
                aboutThisLearner.Add(opp.Id);
            }
        }

        // Filter expired and NotRelevant
        var now = DateTime.UtcNow;
        var valid = all
            .Where(o => (!o.Deadline.HasValue || o.Deadline.Value >= now.AddDays(-1)) &&
                        o.UserTrackingStatus != OpportunityTrackingStatus.NotRelevant)
            .ToList();

        // Only what Radar can say something about.
        //
        // Every opportunity started at 48 points with a floor of 38, so one matching nothing
        // about this learner still came back at about half and sat in the Explore tier. Asked for
        // opportunities, somebody studying nursing was shown accelerator programmes because the
        // list was long enough to fill a page. An opportunity nobody can explain the presence of
        // is not a weak match - it is noise with a percentage next to it.
        //
        // A learner who has stated nothing gets nothing here, and the screen says to personalise.
        // That is the same rule as the Intelligence feed, and it is the honest one: Radar asks
        // what somebody wants rather than deriving it.
        valid = valid.Where(o => aboutThisLearner.Contains(o.Id)).ToList();

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
        var pool = fresh.Count >= DailyBatchSize ? fresh : valid;

        // Best first, then unseen before seen at the same score. The tiers are a label on the
        // score rather than a separate ranking, so slicing six from one and four from another
        // produced the same order by a longer route.
        var ranked = pool
            .OrderByDescending(o => o.MatchScorePercent)
            .ThenByDescending(o => o.IsNewToYou)
            .Concat(valid.OrderByDescending(o => o.MatchScorePercent))
            .DistinctBy(o => o.Id)
            .ToList();

        // Places kept for opportunities the reader can actually take.
        //
        // Every source but one is global, pan-African or remote, so ranking by score alone filled
        // the page with foreign matches - a fellowship in Berlin, a scholarship to the United
        // States, an accelerator in London. Each real, each a long shot, and offered to somebody
        // in Lagos who also needs to know what is open to them this month in Lagos.
        //
        // A floor rather than a quota: the strongest matches still lead, a day that is mostly
        // local stays mostly local, and a reader with nothing near them gets a full page rather
        // than a short one. See OpportunityMix.
        var where = $"{profile.City} {profile.Region} {learningRecord?.Location}".Trim();

        var curated = OpportunityMix.Curate(
            ranked,
            o => OpportunityMix.IsNearby(o.RegionTag, o.IsRemote, where),
            DailyBatchSize);

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

        // An empty day is not stored.
        //
        // GetDailyFeedAsync hydrates only a batch with ids in it, so storing an empty one meant
        // every request from a learner with nothing matchable skipped hydration and rebuilt in
        // full - a 500-document scan, four more round trips and an upsert, to return nothing. The
        // write was pure cost for exactly the people getting the least.
        if (curated.Count == 0) return feed;

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

    /// <summary>
    /// Scores one opportunity for one learner, and says whether anything about them matched.
    /// </summary>
    /// <remarks>
    /// The return value is the thing the feed needs and did not have. Every opportunity started
    /// at 48 and the score was clamped to a floor of 38, so one that matched nothing about the
    /// learner still came back at about half and landed in the Explore tier - and the daily batch
    /// then padded itself to twelve from whatever was left. So the feed was full every day
    /// whether or not Radar had anything to say about any of it.
    ///
    /// What counts as being about the learner: the field they are studying, an interest they
    /// stated, a goal they stated, the role the record says they are aiming for, or a topic they
    /// are working on next. Not the deadline, and not the kind of thing it is - a conference
    /// closing on Friday is urgent and generic at the same time.
    /// </remarks>
    private bool EnrichOpportunityWithPersonalization(
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
        // Read through LearningIntentReader, which exists to keep a goal out of this slot and
        // documents its Domain as "never the outcome".
        //
        // This read PrimaryGoal directly. For somebody whose goal is "find a job", the domain
        // became "a job" - so every job advert ever written matched it on the word "job", scored
        // twenty-six, and was announced as "It is a job work, which is what you are learning."
        // Reported from production by a learner whose interest is International Relations, shown
        // three senior AI engineering roles at ninety per cent. The learning path had been
        // taught this distinction; opportunity matching never was.
        var intent = LearningIntentReader.Read(profile);
        var domain = Firm(activeRoadmap?.Interest) ?? FieldWorthMatching(intent.Domain);

        // A field signal is the thing that makes an opportunity theirs rather than merely open.
        // Tracked separately from `personal` because `personal` also counts a deadline they
        // asked about or a type that suits their stage - true things, but not about their field.
        var fieldMatched = false;

        if (domain is not null && DomainVocabulary.MentionsSubject(fullText, domain))
        {
            score += 26;
            fieldMatched = true;
            signals.Add(new MatchSignal(26, $"It is {SubjectPhrase.Lower(domain)} work, which is what you are learning."));
        }

        // By word rather than by substring. A plain Contains on a stated interest matched any
        // advert with those letters in it anywhere: "art" hit every "start date", "AI" hit
        // "said". MentionsSubject matches whole words and known forms of them, which is the same
        // rule the feed uses against the same kind of text.
        var personal = domain is not null && signals.Count > 0;

        foreach (var interest in OpportunityFit.Matching(fullText, profile.Interests))
        {
            score += 8;
            personal = true;
            fieldMatched = true;
            signals.Add(new MatchSignal(8, $"You said you are interested in {SubjectPhrase.Lower(interest)}."));
        }

        // What they said they are trying to do. Goals were in the profile, used by the roadmap
        // and by the Intelligence feed, and read here only as a fallback source of one domain
        // string - so somebody whose goal was a scholarship was never matched on it.
        foreach (var goal in OpportunityFit.Matching(fullText, profile.Goals.Where(LearningIntentReader.NamesASubject)))
        {
            score += 12;
            personal = true;
            // The learner's own wording, quoted rather than inflected. A goal is a phrase - "win
            // a scholarship", "move into data work" - and reading it into the middle of a
            // sentence produces "a step toward win a scholarship".
            signals.Add(new MatchSignal(12, $"It fits what you said you are working on: {SubjectPhrase.Lower(goal)}."));
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

        // What the type of thing says about the fit, as a table rather than a switch buried
        // here. See OpportunityFit: Graduate was missing from it entirely, and conferences,
        // seminars, workshops and job fairs scored the no-match default for every persona at
        // once.
        // Only a stage they chose. PersonaType's default is Student, so an account that was
        // never asked scored as one - and was then told "it suits where you are now" about a
        // stage nobody stated. The feed was fixed for this; the opportunities list was making
        // the same claim in the same words. See UserProfile.HasChosenCategory.
        //
        // The things anybody can turn up to still score, because that needs no stage: a
        // conference in your field is worth a Saturday whoever you are.
        var openToAnyone = OpportunityFit.OpenToAnyone.Contains(opp.Type);
        var personaCounts = profile.HasChosenCategory || openToAnyone;

        var personaPoints = personaCounts
            ? OpportunityFit.PointsFor(profile.Persona, opp.Type)
            : OpportunityFit.NoSignal;
        score += personaPoints;

        if (personaCounts && OpportunityFit.IsASignal(profile.Persona, opp.Type))
        {
            signals.Add(new MatchSignal(
                personaPoints,
                openToAnyone
                    ? "It is open to anyone who wants to turn up."
                    : "It is the kind of opening that suits where you are now."));
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
                personal = true;
                fieldMatched = true;
                score += 12;
                signals.Add(new MatchSignal(12, $"It is a step toward {SubjectPhrase.Lower(role)}, the role you are aiming for."));
            }

            var priority = record.PriorityTopics.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && fullText.Contains(t.ToLowerInvariant()));
            if (priority is not null)
            {
                personal = true;
                fieldMatched = true;
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

        // Nothing reads as a strong match without a field signal.
        //
        // The backstop, and the part of this fix that does not depend on getting a vocabulary
        // right. Three senior AI engineering roles were shown at ninety per cent to a learner
        // whose interest is International Relations, because a goal had been admitted as a field
        // and then matched on the word "job". The two causes above are fixed; this makes the
        // symptom impossible to reach by some other route, because a percentage is a claim and a
        // high one about somebody's field has to have been earned by something that read their
        // field.
        //
        // Not zero, and not hidden. An open conference, a near deadline and a type that suits
        // their stage are all true and all worth seeing - they are simply not evidence that the
        // thing is in their field, so they cannot carry a number that says it is.
        opp.MatchScorePercent = fieldMatched
            ? Math.Clamp(score, 38, 98)
            : Math.Clamp(score, 38, MostWithoutAFieldSignal);
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

        // Something the learner already asked for, or already said something about, counts too:
        // an opportunity they marked interesting is about them by definition.
        return personal || opp.UserTrackingStatus is OpportunityTrackingStatus.Interested
            or OpportunityTrackingStatus.Saved
            or OpportunityTrackingStatus.Applied
            or OpportunityTrackingStatus.Accepted;
    }

    /// <summary>A reason the score moved, written the way the user will read it.</summary>
    private sealed record MatchSignal(int Points, string Reason);

    /// <summary>How many opportunities a day holds.</summary>
    private const int DailyBatchSize = 12;

    /// <summary>Where everyone starts, before anything is known about the fit.</summary>
    private const int BaseScore = 48;

    /// <summary>
    /// The most an opportunity can score when nothing about the learner's field matched.
    /// </summary>
    /// <remarks>
    /// Deliberately below the band a reader treats as "this is for me". It leaves room for the
    /// things that are genuinely true without a field match - a deadline this week, a type that
    /// suits their stage, something they marked interesting - while making it arithmetically
    /// impossible to show ninety per cent against a field nothing read.
    /// </remarks>
    private const int MostWithoutAFieldSignal = 70;

    /// <summary>
    /// A field worth matching an advert against, or null.
    /// </summary>
    /// <remarks>
    /// LearningIntentReader returns a placeholder when it has no field to name. It reads
    /// correctly inside a sentence and is useless as a search term - every advert matches "your
    /// field" about as well as every other - so it is not one.
    /// </remarks>
    private static string? FieldWorthMatching(string? domain) =>
        string.IsNullOrWhiteSpace(domain) || domain == LearningIntentReader.NoFieldNamed
            ? null
            : domain;

    /// <summary>
    /// How much of a stored batch has to survive re-checking before it is served as today's.
    /// </summary>
    /// <remarks>
    /// Below this it is rebuilt. A third of a day is closer to a bug than to a short day, and the
    /// rebuild is one scan.
    /// </remarks>
    private const int MinimumHydratedBatch = 6;

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

    // Sixteen hand-written opportunities used to be inserted here, and they are gone.
    //
    // Five were programmes that do not exist, attributed to organisations that do: a "Google
    // Cybersecurity Professional Fellowship" from "Google Security Team & Africa Digital Hub"
    // paying a USD 2,500 monthly stipend, a "DeepMind Africa AI & Machine Learning Scholarship",
    // a HackerOne challenge run with the "Cyber Africa Alliance". Three were job openings
    // invented at Paystack, Cloudflare and Stripe. The remaining eight were real — Chevening,
    // Mastercard Foundation Scholars, Tony Elumelu, Y Combinator — carrying invented eligibility
    // notes, selection processes and, in every single case, a deadline of
    // DateTime.UtcNow.AddDays(n): always a fortnight away, whenever you looked.
    //
    // Radar tells students in Africa to apply for these. A learner could build a plan around a
    // funded fellowship that was never offered, or miss Chevening's real deadline because the
    // one on screen was generated when the page loaded.
    //
    // The seeding was also destructive. It ran whenever the collection held fewer than fifteen
    // rows and began by deleting every opportunity in it — so a thin but real harvest from
    // OpportunityIngestionService was wiped to reinstate the invented set.
}

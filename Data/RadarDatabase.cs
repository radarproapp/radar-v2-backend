using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data.Documents;
using RadarV2.Models;

namespace RadarV2.Data;

public class RadarDatabase
{
    private readonly IMongoDatabase _db;

    static RadarDatabase()
    {
        // Before any collection is touched: the driver caches a class map the first time it sees
        // a type, so a convention registered later would not apply to it.
        BsonConventions.Register();
    }

    public RadarDatabase(IConfiguration config)
    {
        var connectionString = config["MongoDB:ConnectionString"] ?? "mongodb://localhost:27017";
        var databaseName     = config["MongoDB:DatabaseName"] ?? "radar";

        // A pool with a ceiling, and a wait rather than a hang.
        //
        // The driver's default pool is a hundred connections per process and it will open them
        // all under load. Atlas caps connections per cluster - on the shared tiers that cap is
        // low - so one busy instance can take the whole allowance, and the next instance, the
        // cron job and the admin console all get refused. The failure does not look like load:
        // it looks like the database being down.
        //
        // The wait timeout matters as much as the size. Without one, a request that cannot get a
        // connection blocks until something else finishes, which turns a slow database into a
        // hung web server; with one it fails in five seconds and says so.
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.MaxConnectionPoolSize = config.GetValue("MongoDB:MaxPoolSize", 40);
        settings.MinConnectionPoolSize = config.GetValue("MongoDB:MinPoolSize", 2);
        settings.WaitQueueTimeout = TimeSpan.FromSeconds(5);
        settings.ConnectTimeout = TimeSpan.FromSeconds(10);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

        var client = new MongoClient(settings);
        _db = client.GetDatabase(databaseName);

        // Run index creation in background so cold boot and the first HTTP request are instantaneous
        _ = Task.Run(() =>
        {
            try
            {
                EnsureIndexes();
            }
            catch
            {
                // Non-fatal if index creation fails or races during startup
            }
        });
    }

    // ── Typed collection accessors ──────────────────────────────────────────
    public IMongoCollection<UserDocument>  Users        => _db.GetCollection<UserDocument>("users");
    public IMongoCollection<UserProfile>   Profiles     => _db.GetCollection<UserProfile>("profiles");
    public IMongoCollection<ContentItem>   ContentItems => _db.GetCollection<ContentItem>("content_items");
    public IMongoCollection<Opportunity>   Opportunities => _db.GetCollection<Opportunity>("opportunities");
    public IMongoCollection<GrowthRoadmap> Roadmaps     => _db.GetCollection<GrowthRoadmap>("roadmaps");
    public IMongoCollection<SavedItemDoc>        SavedItems          => _db.GetCollection<SavedItemDoc>("saved_items");
    public IMongoCollection<DismissedItemDoc>    DismissedItems      => _db.GetCollection<DismissedItemDoc>("dismissed_items");
    public IMongoCollection<SavedOpportunityDoc> SavedOpportunities  => _db.GetCollection<SavedOpportunityDoc>("saved_opportunities");
    public IMongoCollection<AppliedOpportunityDoc> AppliedOpportunities => _db.GetCollection<AppliedOpportunityDoc>("applied_opportunities");
    public IMongoCollection<OpportunityTrackingDoc> OpportunityTrackings => _db.GetCollection<OpportunityTrackingDoc>("opportunity_trackings");
    public IMongoCollection<PlaybackProgressDoc> PlaybackProgress  => _db.GetCollection<PlaybackProgressDoc>("playback_progress");
    public IMongoCollection<DocumentAssessment> DocumentAssessments => _db.GetCollection<DocumentAssessment>("document_assessments");
    public IMongoCollection<PasswordResetDoc> PasswordResets => _db.GetCollection<PasswordResetDoc>("password_resets");
    public IMongoCollection<ChatSession>   ChatSessions => _db.GetCollection<ChatSession>("chat_sessions");
    public IMongoCollection<WeeklyBrief>   WeeklyBriefs     => _db.GetCollection<WeeklyBrief>("weekly_briefs");
    public IMongoCollection<LibraryDocument>    LibraryDocuments => _db.GetCollection<LibraryDocument>("library_documents");
    public IMongoCollection<IngestionQuotaDoc> IngestionQuotas  => _db.GetCollection<IngestionQuotaDoc>("ingestion_quotas");
    public IMongoCollection<SourceProfile>     Sources         => _db.GetCollection<SourceProfile>("sources");
    public IMongoCollection<TopicProfile>      Topics          => _db.GetCollection<TopicProfile>("topics");
    public IMongoCollection<Note>              Notes           => _db.GetCollection<Note>("notes");
    public IMongoCollection<CapturedItem>       CapturedItems   => _db.GetCollection<CapturedItem>("captured_items");
    public IMongoCollection<PolicyComparison>   Comparisons     => _db.GetCollection<PolicyComparison>("comparisons");
    public IMongoCollection<ProjectTemplate>    ProjectTemplates => _db.GetCollection<ProjectTemplate>("project_templates");
    public IMongoCollection<StudioProject>      UserProjects    => _db.GetCollection<StudioProject>("user_projects");
    public IMongoCollection<UserSubscriptionDoc> UserSubscriptions => _db.GetCollection<UserSubscriptionDoc>("user_subscriptions");
    public IMongoCollection<AnalyticsEvent> AnalyticsEvents => _db.GetCollection<AnalyticsEvent>("analytics_events");
    public IMongoCollection<UserContentWhy> UserContentWhys => _db.GetCollection<UserContentWhy>("user_content_why");
    public IMongoCollection<ContentReport> ContentReports => _db.GetCollection<ContentReport>("content_reports");
    public IMongoCollection<BehavioralSignal> BehavioralSignals => _db.GetCollection<BehavioralSignal>("behavioral_signals");
    public IMongoCollection<YouTubeChannelDoc> YouTubeChannels => _db.GetCollection<YouTubeChannelDoc>("youtube_channels");
    public IMongoCollection<MentorQuiz> MentorQuizzes => _db.GetCollection<MentorQuiz>("mentor_quizzes");
    public IMongoCollection<MentorStudyPlan> MentorStudyPlans => _db.GetCollection<MentorStudyPlan>("mentor_study_plans");
    public IMongoCollection<TopicMastery> TopicMasteries => _db.GetCollection<TopicMastery>("topic_masteries");
    public IMongoCollection<SpacedRevisionItem> SpacedRevisions => _db.GetCollection<SpacedRevisionItem>("spaced_revisions");
    public IMongoCollection<AiActivityLogDoc> AiActivities => _db.GetCollection<AiActivityLogDoc>("ai_activities");
    public IMongoCollection<YouTubeSearchCacheDoc> YouTubeSearches => _db.GetCollection<YouTubeSearchCacheDoc>("youtube_searches");
    public IMongoCollection<ErrorLogDoc> ErrorLog => _db.GetCollection<ErrorLogDoc>("error_log");
    public IMongoCollection<SourceHealthDoc> SourceHealth => _db.GetCollection<SourceHealthDoc>("source_health");
    public IMongoCollection<OpportunityImpressionDoc> OpportunityImpressions => _db.GetCollection<OpportunityImpressionDoc>("opportunity_impressions");
    public IMongoCollection<DailyOpportunityBatchDoc> DailyOpportunityBatches => _db.GetCollection<DailyOpportunityBatchDoc>("daily_opportunity_batches");

    private void EnsureIndexes()
    {
        // Expire after a fortnight: these exist to diagnose what is breaking now, and an
        // unbounded error log is its own outage waiting to happen.
        ErrorLog.Indexes.CreateOne(
            new CreateIndexModel<ErrorLogDoc>(
                Builders<ErrorLogDoc>.IndexKeys.Ascending(e => e.OccurredAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(14) }));

        Users.Indexes.CreateOne(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true }));

        ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys.Ascending(c => c.Type)));

        ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys.Ascending(c => c.UrlHash),
                new CreateIndexOptions { Sparse = true }));

        ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys
                    .Ascending(c => c.Layer)
                    .Descending(c => c.PublishedAt)));

        // Serves the feed's keyset sort — (PublishedAt desc, _id desc) — so Mongo can walk the index
        // in order instead of materialising and sorting the collection in memory. This is what makes
        // the cursor cheap: the predicate seeks straight to a position rather than skipping N rows.
        ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys
                    .Descending(c => c.PublishedAt)
                    .Descending(c => c.Id)));

        SavedItems.Indexes.CreateOne(
            new CreateIndexModel<SavedItemDoc>(
                Builders<SavedItemDoc>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.ContentItemId),
                new CreateIndexOptions { Unique = true }));

        DismissedItems.Indexes.CreateOne(
            new CreateIndexModel<DismissedItemDoc>(
                Builders<DismissedItemDoc>.IndexKeys
                    .Ascending(d => d.UserId)
                    .Ascending(d => d.ContentItemId),
                new CreateIndexOptions { Unique = true }));

        LibraryDocuments.Indexes.CreateOne(
            new CreateIndexModel<LibraryDocument>(
                Builders<LibraryDocument>.IndexKeys
                    .Ascending(d => d.Category)
                    .Descending(d => d.Year)));

        SavedOpportunities.Indexes.CreateOne(
            new CreateIndexModel<SavedOpportunityDoc>(
                Builders<SavedOpportunityDoc>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.OpportunityId),
                new CreateIndexOptions { Unique = true }));

        // Supports the expiry filter the opportunity list applies before per-user scoring, so that
        // query never degrades into a full collection scan.
        Opportunities.Indexes.CreateOne(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Ascending(o => o.Deadline)));

        // The order the scoring window is taken in.
        //
        // That window is the five hundred most recently discovered opportunities, and with no
        // index the server had to fetch and sort every non-expired document to find them - on
        // every opportunities page and every first page of the feed. Most job postings carry no
        // deadline, so "non-expired" is nearly the whole collection.
        Opportunities.Indexes.CreateOne(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Descending(o => o.DiscoveredAt)));

        // The link is the identity, and ingestion checks it once per entry - up to forty times
        // per feed, six feeds a run. Unindexed that is a collection scan each time; unique, the
        // database enforces the thing the check is for rather than trusting a read that another
        // run could already have invalidated.
        Opportunities.Indexes.CreateOne(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Ascending(o => o.Url),
                new CreateIndexOptions { Unique = true, Sparse = true }));

        DailyOpportunityBatches.Indexes.CreateOne(
            new CreateIndexModel<DailyOpportunityBatchDoc>(
                Builders<DailyOpportunityBatchDoc>.IndexKeys
                    .Ascending(d => d.UserId)
                    .Descending(d => d.Date)));

        OpportunityImpressions.Indexes.CreateOne(
            new CreateIndexModel<OpportunityImpressionDoc>(
                Builders<OpportunityImpressionDoc>.IndexKeys.Ascending(d => d.UserId)));

        // Data repairs used to sit here, and that was the bug.
        //
        // This method runs on a fire-and-forget Task.Run inside a bare catch, so a failure
        // anywhere in it silently abandons everything after — and there is no way to learn
        // whether a given step ran. Two repairs were lost that way, one of them the deletion of
        // six invented opportunities that stayed on the page for a day afterwards.
        //
        // They live in StartupRepairService now, which logs each one and reports the outcome on
        // /api/version.

        // Looked up by hash on redemption, and swept by expiry so spent tokens do not accumulate.
        PasswordResets.Indexes.CreateOne(
            new CreateIndexModel<PasswordResetDoc>(
                Builders<PasswordResetDoc>.IndexKeys.Ascending(r => r.TokenHash)));

        PasswordResets.Indexes.CreateOne(
            new CreateIndexModel<PasswordResetDoc>(
                Builders<PasswordResetDoc>.IndexKeys.Ascending(r => r.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));

        // One row per user per item, upserted on every heartbeat, and read on every media page.
        PlaybackProgress.Indexes.CreateOne(
            new CreateIndexModel<PlaybackProgressDoc>(
                Builders<PlaybackProgressDoc>.IndexKeys
                    .Ascending(p => p.UserId)
                    .Ascending(p => p.ContentItemId),
                new CreateIndexOptions { Unique = true }));

        AppliedOpportunities.Indexes.CreateOne(
            new CreateIndexModel<AppliedOpportunityDoc>(
                Builders<AppliedOpportunityDoc>.IndexKeys
                    .Ascending(a => a.UserId)
                    .Ascending(a => a.OpportunityId),
                new CreateIndexOptions { Unique = true }));

        IngestionQuotas.Indexes.CreateOne(
            new CreateIndexModel<IngestionQuotaDoc>(
                Builders<IngestionQuotaDoc>.IndexKeys
                    .Ascending(q => q.Service)
                    .Ascending(q => q.Month),
                new CreateIndexOptions { Unique = true }));

        Sources.Indexes.CreateOne(
            new CreateIndexModel<SourceProfile>(
                Builders<SourceProfile>.IndexKeys.Ascending(s => s.Name),
                new CreateIndexOptions { Unique = true }));

        Topics.Indexes.CreateOne(
            new CreateIndexModel<TopicProfile>(
                Builders<TopicProfile>.IndexKeys.Ascending(t => t.Name),
                new CreateIndexOptions { Unique = true }));

        Notes.Indexes.CreateOne(
            new CreateIndexModel<Note>(
                Builders<Note>.IndexKeys
                    .Ascending(n => n.UserId)
                    .Descending(n => n.EditedAt)));

        CapturedItems.Indexes.CreateOne(
            new CreateIndexModel<CapturedItem>(
                Builders<CapturedItem>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.CapturedAt)));

        Comparisons.Indexes.CreateOne(
            new CreateIndexModel<PolicyComparison>(
                Builders<PolicyComparison>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.CreatedAt)));

        // Lets the cache sweep by age without scanning; the TTL itself is enforced in code so a
        // stale entry can still be served if a later lookup needs a fallback.
        YouTubeSearches.Indexes.CreateOne(
            new CreateIndexModel<YouTubeSearchCacheDoc>(
                Builders<YouTubeSearchCacheDoc>.IndexKeys.Descending(d => d.CachedAt)));

        UserProjects.Indexes.CreateOne(
            new CreateIndexModel<StudioProject>(
                Builders<StudioProject>.IndexKeys
                    .Ascending(p => p.UserId)
                    .Descending(p => p.CreatedAt)));

        UserSubscriptions.Indexes.CreateOne(
            new CreateIndexModel<UserSubscriptionDoc>(
                Builders<UserSubscriptionDoc>.IndexKeys
                    .Ascending(s => s.UserId),
                new CreateIndexOptions { Unique = true }));

        AnalyticsEvents.Indexes.CreateOne(
            new CreateIndexModel<AnalyticsEvent>(
                Builders<AnalyticsEvent>.IndexKeys
                    .Ascending(e => e.Type)
                    .Descending(e => e.CreatedAt)));

        AnalyticsEvents.Indexes.CreateOne(
            new CreateIndexModel<AnalyticsEvent>(
                Builders<AnalyticsEvent>.IndexKeys
                    .Ascending(e => e.UserId)
                    .Descending(e => e.CreatedAt)));

        UserContentWhys.Indexes.CreateOne(
            new CreateIndexModel<UserContentWhy>(
                Builders<UserContentWhy>.IndexKeys
                    .Ascending(w => w.UserId)
                    .Ascending(w => w.ContentItemId),
                new CreateIndexOptions { Unique = true }));

        ContentReports.Indexes.CreateOne(
            new CreateIndexModel<ContentReport>(
                Builders<ContentReport>.IndexKeys
                    .Ascending(r => r.IsResolved)
                    .Descending(r => r.CreatedAt)));

        ContentReports.Indexes.CreateOne(
            new CreateIndexModel<ContentReport>(
                Builders<ContentReport>.IndexKeys.Ascending(r => r.Source)));

        BehavioralSignals.Indexes.CreateOne(
            new CreateIndexModel<BehavioralSignal>(
                Builders<BehavioralSignal>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.Term)
                    .Ascending(s => s.Source),
                new CreateIndexOptions { Unique = true }));

        BehavioralSignals.Indexes.CreateOne(
            new CreateIndexModel<BehavioralSignal>(
                Builders<BehavioralSignal>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Descending(s => s.UpdatedAt)));
    }
}

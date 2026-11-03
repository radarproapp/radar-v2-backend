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

        Start(config);
    }

    /// <summary>
    /// A database on a client somebody else owns.
    /// </summary>
    /// <remarks>
    /// For tests, which build one of these per test case against a throwaway database. The
    /// ordinary constructor opens a <see cref="MongoClient"/> each time, and a client is a
    /// connection pool - forty sockets by default - so a suite of a hundred and thirty
    /// integration tests asked one mongod for several thousand connections and got
    /// "Timed out waiting for a connection" across twenty-three unrelated tests. Sharing the
    /// client shares the pool.
    /// </remarks>
    public RadarDatabase(IMongoClient client, string databaseName, IConfiguration config)
    {
        _db = client.GetDatabase(databaseName);
        Start(config);
    }

    private void Start(IConfiguration config)
    {
        // Skippable, for tests.
        //
        // Forty-three index creations per database is wasted work against a database that holds
        // a handful of documents and is thrown away at the end of the test. The tests that
        // genuinely depend on an index build it themselves in their own constructor, and say why:
        // this runs fire-and-forget, so relying on it would make the test a race.
        if (!config.GetValue("MongoDB:EnsureIndexes", true)) return;

        // Run index creation in background so cold boot and the first HTTP request are instantaneous
        _ = Task.Run(() =>
        {
            try
            {
                EnsureIndexes();
            }
            catch (Exception failure)
            {
                // Non-fatal if index creation fails or races during startup, but not silent.
                // Each creation is wrapped individually by Safely, so reaching here means
                // something outside one of them went wrong.
                Record("EnsureIndexes", failure);
            }
        });
    }

    /// <summary>
    /// Indexes that were not created, and why.
    /// </summary>
    /// <remarks>
    /// Static because the failure outlives the request that caused it and nothing else would
    /// ever know. An index missing in production looks exactly like a slow query: the collection
    /// scans, the page takes four seconds, and every test stays green because the test database
    /// is small enough not to care.
    /// </remarks>
    private static readonly List<string> IndexFailures = [];

    /// <summary>
    /// What index creation could not do, for the version endpoint to report.
    /// </summary>
    /// <remarks>
    /// The collection and the kind of failure, and not the driver's message. A duplicate-key
    /// message names the colliding value - <c>dup key: { ReferralCode: "ABC2345" }</c>, or an
    /// email address - and the endpoint reporting this is unauthenticated. The full message goes
    /// to the log, where it is already behind the platform's own access control.
    /// </remarks>
    public static IReadOnlyList<string> MissingIndexes
    {
        get { lock (IndexFailures) return [.. IndexFailures]; }
    }

    private static void Record(string what, Exception failure)
    {
        var safeToPublish = $"{what}: {failure.GetType().Name}";
        lock (IndexFailures)
        {
            if (!IndexFailures.Contains(safeToPublish)) IndexFailures.Add(safeToPublish);
        }

        // Console, because this runs from a constructor with no logger in reach and the
        // alternative is nothing at all. The message belongs here and not in the response.
        Console.Error.WriteLine($"[index] {safeToPublish} — {failure.Message}");
    }

    /// <summary>
    /// Creates one index, and lets the next one be attempted if it fails.
    /// </summary>
    /// <remarks>
    /// Forty-three index creations ran as one straight-line method, so the first exception
    /// abandoned every index after it - inside a fire-and-forget task with an empty catch, which
    /// is the exact failure StartupRepairService was written to stop doing: "an exception
    /// anywhere in that long method silently abandons everything after it, and there is no way
    /// to find out whether a repair ran - which is exactly what happened."
    ///
    /// One duplicate-key collision on pre-existing data in any of the thirteen unique indexes
    /// was enough to leave the collections declared after it unindexed, with no log line and
    /// nothing to query.
    /// </remarks>
    private static void Safely(string collection, Action create)
    {
        try
        {
            create();
        }
        catch (Exception failure)
        {
            Record(collection, failure);
        }
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

    /// <summary>What participants made about Radar, somewhere else.</summary>
    public IMongoCollection<ContentSubmission> ContentSubmissions => _db.GetCollection<ContentSubmission>("content_submissions");
    public IMongoCollection<AdminGrant> AdminGrants => _db.GetCollection<AdminGrant>("admin_grants");
    public IMongoCollection<UserContentWhy> UserContentWhys => _db.GetCollection<UserContentWhy>("user_content_why");
    public IMongoCollection<ContentReport> ContentReports => _db.GetCollection<ContentReport>("content_reports");
    public IMongoCollection<BehavioralSignal> BehavioralSignals => _db.GetCollection<BehavioralSignal>("behavioral_signals");
    public IMongoCollection<YouTubeChannelDoc> YouTubeChannels => _db.GetCollection<YouTubeChannelDoc>("youtube_channels");
    public IMongoCollection<MentorQuiz> MentorQuizzes => _db.GetCollection<MentorQuiz>("mentor_quizzes");
    public IMongoCollection<MentorStudyPlan> MentorStudyPlans => _db.GetCollection<MentorStudyPlan>("mentor_study_plans");
    public IMongoCollection<MentorConversation> MentorConversations => _db.GetCollection<MentorConversation>("mentor_conversations");
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
        Safely("ErrorLog", () => ErrorLog.Indexes.CreateOne(
            new CreateIndexModel<ErrorLogDoc>(
                Builders<ErrorLogDoc>.IndexKeys.Ascending(e => e.OccurredAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(14) })));

        Safely("Users", () => Users.Indexes.CreateOne(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true })));

        Safely("ContentItems", () => ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys.Ascending(c => c.Type))));

        Safely("ContentItems", () => ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys.Ascending(c => c.UrlHash),
                new CreateIndexOptions { Sparse = true })));

        Safely("ContentItems", () => ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys
                    .Ascending(c => c.Layer)
                    .Descending(c => c.PublishedAt))));

        // Serves the feed's keyset sort — (PublishedAt desc, _id desc) — so Mongo can walk the index
        // in order instead of materialising and sorting the collection in memory. This is what makes
        // the cursor cheap: the predicate seeks straight to a position rather than skipping N rows.
        Safely("ContentItems", () => ContentItems.Indexes.CreateOne(
            new CreateIndexModel<ContentItem>(
                Builders<ContentItem>.IndexKeys
                    .Descending(c => c.PublishedAt)
                    .Descending(c => c.Id))));

        Safely("SavedItems", () => SavedItems.Indexes.CreateOne(
            new CreateIndexModel<SavedItemDoc>(
                Builders<SavedItemDoc>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.ContentItemId),
                new CreateIndexOptions { Unique = true })));

        Safely("DismissedItems", () => DismissedItems.Indexes.CreateOne(
            new CreateIndexModel<DismissedItemDoc>(
                Builders<DismissedItemDoc>.IndexKeys
                    .Ascending(d => d.UserId)
                    .Ascending(d => d.ContentItemId),
                new CreateIndexOptions { Unique = true })));

        Safely("LibraryDocuments", () => LibraryDocuments.Indexes.CreateOne(
            new CreateIndexModel<LibraryDocument>(
                Builders<LibraryDocument>.IndexKeys
                    .Ascending(d => d.Category)
                    .Descending(d => d.Year))));

        Safely("SavedOpportunities", () => SavedOpportunities.Indexes.CreateOne(
            new CreateIndexModel<SavedOpportunityDoc>(
                Builders<SavedOpportunityDoc>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.OpportunityId),
                new CreateIndexOptions { Unique = true })));

        // Supports the expiry filter the opportunity list applies before per-user scoring, so that
        // query never degrades into a full collection scan.
        Safely("Opportunities", () => Opportunities.Indexes.CreateOne(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Ascending(o => o.Deadline))));

        // The order the scoring window is taken in.
        //
        // That window is the five hundred most recently discovered opportunities, and with no
        // index the server had to fetch and sort every non-expired document to find them - on
        // every opportunities page and every first page of the feed. Most job postings carry no
        // deadline, so "non-expired" is nearly the whole collection.
        Safely("Opportunities", () => Opportunities.Indexes.CreateOne(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Descending(o => o.DiscoveredAt))));

        // The link is the identity, and ingestion checks it once per entry - up to forty times
        // per feed, six feeds a run. Unindexed that is a collection scan each time; unique, the
        // database enforces the thing the check is for rather than trusting a read that another
        // run could already have invalidated.
        Safely("Opportunities", () => Opportunities.Indexes.CreateOne(
            new CreateIndexModel<Opportunity>(
                Builders<Opportunity>.IndexKeys.Ascending(o => o.Url),
                new CreateIndexOptions { Unique = true, Sparse = true })));

        Safely("DailyOpportunityBatches", () => DailyOpportunityBatches.Indexes.CreateOne(
            new CreateIndexModel<DailyOpportunityBatchDoc>(
                Builders<DailyOpportunityBatchDoc>.IndexKeys
                    .Ascending(d => d.UserId)
                    .Descending(d => d.Date))));

        Safely("OpportunityImpressions", () => OpportunityImpressions.Indexes.CreateOne(
            new CreateIndexModel<OpportunityImpressionDoc>(
                Builders<OpportunityImpressionDoc>.IndexKeys.Ascending(d => d.UserId))));

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
        Safely("PasswordResets", () => PasswordResets.Indexes.CreateOne(
            new CreateIndexModel<PasswordResetDoc>(
                Builders<PasswordResetDoc>.IndexKeys.Ascending(r => r.TokenHash))));

        Safely("PasswordResets", () => PasswordResets.Indexes.CreateOne(
            new CreateIndexModel<PasswordResetDoc>(
                Builders<PasswordResetDoc>.IndexKeys.Ascending(r => r.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero })));

        // One row per user per item, upserted on every heartbeat, and read on every media page.
        Safely("PlaybackProgress", () => PlaybackProgress.Indexes.CreateOne(
            new CreateIndexModel<PlaybackProgressDoc>(
                Builders<PlaybackProgressDoc>.IndexKeys
                    .Ascending(p => p.UserId)
                    .Ascending(p => p.ContentItemId),
                new CreateIndexOptions { Unique = true })));

        Safely("AppliedOpportunities", () => AppliedOpportunities.Indexes.CreateOne(
            new CreateIndexModel<AppliedOpportunityDoc>(
                Builders<AppliedOpportunityDoc>.IndexKeys
                    .Ascending(a => a.UserId)
                    .Ascending(a => a.OpportunityId),
                new CreateIndexOptions { Unique = true })));

        Safely("IngestionQuotas", () => IngestionQuotas.Indexes.CreateOne(
            new CreateIndexModel<IngestionQuotaDoc>(
                Builders<IngestionQuotaDoc>.IndexKeys
                    .Ascending(q => q.Service)
                    .Ascending(q => q.Month),
                new CreateIndexOptions { Unique = true })));

        Safely("Sources", () => Sources.Indexes.CreateOne(
            new CreateIndexModel<SourceProfile>(
                Builders<SourceProfile>.IndexKeys.Ascending(s => s.Name),
                new CreateIndexOptions { Unique = true })));

        Safely("Topics", () => Topics.Indexes.CreateOne(
            new CreateIndexModel<TopicProfile>(
                Builders<TopicProfile>.IndexKeys.Ascending(t => t.Name),
                new CreateIndexOptions { Unique = true })));

        Safely("Notes", () => Notes.Indexes.CreateOne(
            new CreateIndexModel<Note>(
                Builders<Note>.IndexKeys
                    .Ascending(n => n.UserId)
                    .Descending(n => n.EditedAt))));

        Safely("CapturedItems", () => CapturedItems.Indexes.CreateOne(
            new CreateIndexModel<CapturedItem>(
                Builders<CapturedItem>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.CapturedAt))));

        Safely("Comparisons", () => Comparisons.Indexes.CreateOne(
            new CreateIndexModel<PolicyComparison>(
                Builders<PolicyComparison>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.CreatedAt))));

        // The order the Mentor's conversation list is read in: this learner's threads, most
        // recently used first. Same shape as the notes index above, and there for the same
        // reason - without it the server fetches every conversation the account has ever had and
        // sorts them in memory, on every visit to the Mentor.
        //
        // LastMessageAt descending rather than CreatedAt, because that is the field the sort
        // actually names; an index on the wrong one of the two reads as an index and does
        // nothing.
        Safely("MentorConversations", () => MentorConversations.Indexes.CreateOne(
            new CreateIndexModel<MentorConversation>(
                Builders<MentorConversation>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.LastMessageAt))));

        // Lets the cache sweep by age without scanning; the TTL itself is enforced in code so a
        // stale entry can still be served if a later lookup needs a fallback.
        Safely("YouTubeSearches", () => YouTubeSearches.Indexes.CreateOne(
            new CreateIndexModel<YouTubeSearchCacheDoc>(
                Builders<YouTubeSearchCacheDoc>.IndexKeys.Descending(d => d.CachedAt))));

        Safely("UserProjects", () => UserProjects.Indexes.CreateOne(
            new CreateIndexModel<StudioProject>(
                Builders<StudioProject>.IndexKeys
                    .Ascending(p => p.UserId)
                    .Descending(p => p.CreatedAt))));

        Safely("UserSubscriptions", () => UserSubscriptions.Indexes.CreateOne(
            new CreateIndexModel<UserSubscriptionDoc>(
                Builders<UserSubscriptionDoc>.IndexKeys
                    .Ascending(s => s.UserId),
                new CreateIndexOptions { Unique = true })));

        // A referral code is unique, and most profiles do not have one - issued on request
        // rather than at sign-up. Sparse for that reason: without it, every profile without a
        // code would collide on null with every other one.
        Safely("Profiles", () => Profiles.Indexes.CreateOne(
            new CreateIndexModel<UserProfile>(
                Builders<UserProfile>.IndexKeys.Ascending(p => p.ReferralCode),
                new CreateIndexOptions { Unique = true, Sparse = true })));

        // A participant's own submissions, newest first.
        Safely("ContentSubmissions", () => ContentSubmissions.Indexes.CreateOne(
            new CreateIndexModel<ContentSubmission>(
                Builders<ContentSubmission>.IndexKeys
                    .Ascending(c => c.ProfileId)
                    .Descending(c => c.SubmittedAt))));

        // The review queue: what nobody has marked, oldest first, and the judged tail.
        //
        // Deliberately not sparse, unlike the referral code above. A sparse index omits the
        // documents whose field is absent - which is exactly the set the queue is looking for,
        // since an unjudged submission has no Score element at all. Measured: sparse serves the
        // {$ne: null} half and leaves the {Score: null} half a collection scan.
        Safely("ContentSubmissions", () => ContentSubmissions.Indexes.CreateOne(
            new CreateIndexModel<ContentSubmission>(
                Builders<ContentSubmission>.IndexKeys
                    .Ascending(c => c.Score)
                    .Ascending(c => c.SubmittedAt))));

        Safely("ContentSubmissions", () => ContentSubmissions.Indexes.CreateOne(
            new CreateIndexModel<ContentSubmission>(
                Builders<ContentSubmission>.IndexKeys.Descending(c => c.JudgedAt))));

        // One video, once. Submitting the same link five times was free and used to raise a
        // participant's standing on the board; the key is case folded with the trailing slash
        // dropped, because that is how the same video differs when pasted from two apps.
        Safely("ContentSubmissions", () => ContentSubmissions.Indexes.CreateOne(
            new CreateIndexModel<ContentSubmission>(
                Builders<ContentSubmission>.IndexKeys
                    .Ascending(c => c.ProfileId)
                    .Ascending(c => c.LinkKey),
                new CreateIndexOptions { Unique = true })));

        // One row per address, so granting twice updates rather than duplicates. Not partial on
        // RevokedAt: a revoked grant keeps the address, and re-granting the same person has to
        // land on the row that already carries their history rather than beside it.
        Safely("AdminGrants", () => AdminGrants.Indexes.CreateOne(
            new CreateIndexModel<AdminGrant>(
                Builders<AdminGrant>.IndexKeys.Ascending(g => g.Email),
                new CreateIndexOptions { Unique = true })));

        // The acquisition board groups by who brought somebody in.
        Safely("Profiles", () => Profiles.Indexes.CreateOne(
            new CreateIndexModel<UserProfile>(
                Builders<UserProfile>.IndexKeys.Ascending(p => p.ReferredByUserId))));

        Safely("AnalyticsEvents", () => AnalyticsEvents.Indexes.CreateOne(
            new CreateIndexModel<AnalyticsEvent>(
                Builders<AnalyticsEvent>.IndexKeys
                    .Ascending(e => e.Type)
                    .Descending(e => e.CreatedAt))));

        Safely("AnalyticsEvents", () => AnalyticsEvents.Indexes.CreateOne(
            new CreateIndexModel<AnalyticsEvent>(
                Builders<AnalyticsEvent>.IndexKeys
                    .Ascending(e => e.UserId)
                    .Descending(e => e.CreatedAt))));

        // The window on its own. CreatedAt is a suffix in both indexes above, so neither of them
        // helps a query that filters on nothing but the date - and the admin activity chart does
        // exactly that. Measured: a collection scan over 60,000 events, every document examined.
        Safely("AnalyticsEvents", () => AnalyticsEvents.Indexes.CreateOne(
            new CreateIndexModel<AnalyticsEvent>(
                Builders<AnalyticsEvent>.IndexKeys.Descending(e => e.CreatedAt))));

        Safely("UserContentWhys", () => UserContentWhys.Indexes.CreateOne(
            new CreateIndexModel<UserContentWhy>(
                Builders<UserContentWhy>.IndexKeys
                    .Ascending(w => w.UserId)
                    .Ascending(w => w.ContentItemId),
                new CreateIndexOptions { Unique = true })));

        Safely("ContentReports", () => ContentReports.Indexes.CreateOne(
            new CreateIndexModel<ContentReport>(
                Builders<ContentReport>.IndexKeys
                    .Ascending(r => r.IsResolved)
                    .Descending(r => r.CreatedAt))));

        Safely("ContentReports", () => ContentReports.Indexes.CreateOne(
            new CreateIndexModel<ContentReport>(
                Builders<ContentReport>.IndexKeys.Ascending(r => r.Source))));

        Safely("BehavioralSignals", () => BehavioralSignals.Indexes.CreateOne(
            new CreateIndexModel<BehavioralSignal>(
                Builders<BehavioralSignal>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Ascending(s => s.Term)
                    .Ascending(s => s.Source),
                new CreateIndexOptions { Unique = true })));

        Safely("BehavioralSignals", () => BehavioralSignals.Indexes.CreateOne(
            new CreateIndexModel<BehavioralSignal>(
                Builders<BehavioralSignal>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Descending(s => s.UpdatedAt))));
    }
}

using MongoDB.Driver;
using RadarV2.Data.Documents;
using RadarV2.Models;

namespace RadarV2.Data;

public class RadarDatabase
{
    private readonly IMongoDatabase _db;

    public RadarDatabase(IConfiguration config)
    {
        var connectionString = config["MongoDB:ConnectionString"] ?? "mongodb://localhost:27017";
        var databaseName     = config["MongoDB:DatabaseName"] ?? "radar";
        var client = new MongoClient(connectionString);
        _db = client.GetDatabase(databaseName);
        EnsureIndexes();
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
    public IMongoCollection<ChatSession>   ChatSessions => _db.GetCollection<ChatSession>("chat_sessions");
    public IMongoCollection<WeeklyBrief>   WeeklyBriefs     => _db.GetCollection<WeeklyBrief>("weekly_briefs");
    public IMongoCollection<LibraryDocument>    LibraryDocuments => _db.GetCollection<LibraryDocument>("library_documents");
    public IMongoCollection<IngestionQuotaDoc> IngestionQuotas  => _db.GetCollection<IngestionQuotaDoc>("ingestion_quotas");
    public IMongoCollection<SourceProfile>     Sources         => _db.GetCollection<SourceProfile>("sources");
    public IMongoCollection<TopicProfile>      Topics          => _db.GetCollection<TopicProfile>("topics");
    public IMongoCollection<Note>              Notes           => _db.GetCollection<Note>("notes");
    public IMongoCollection<CapturedItem>       CapturedItems   => _db.GetCollection<CapturedItem>("captured_items");
    public IMongoCollection<PolicyComparison>   Comparisons     => _db.GetCollection<PolicyComparison>("comparisons");
    public IMongoCollection<Clip>               Clips           => _db.GetCollection<Clip>("clips");
    public IMongoCollection<ProjectTemplate>    ProjectTemplates => _db.GetCollection<ProjectTemplate>("project_templates");
    public IMongoCollection<StudioProject>      UserProjects    => _db.GetCollection<StudioProject>("user_projects");
    public IMongoCollection<UserSubscriptionDoc> UserSubscriptions => _db.GetCollection<UserSubscriptionDoc>("user_subscriptions");
    public IMongoCollection<AnalyticsEvent> AnalyticsEvents => _db.GetCollection<AnalyticsEvent>("analytics_events");
    public IMongoCollection<UserContentWhy> UserContentWhys => _db.GetCollection<UserContentWhy>("user_content_why");

    private void EnsureIndexes()
    {
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

        Clips.Indexes.CreateOne(
            new CreateIndexModel<Clip>(
                Builders<Clip>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.PublishedAt)));

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
    }
}

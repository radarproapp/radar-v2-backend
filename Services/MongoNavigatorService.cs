using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoNavigatorService : INavigatorService
{
    private readonly RadarDatabase _db;
    private readonly IOpportunityService _opportunities;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// How long a day's focus is kept, and how much of the cache one entry is worth.
    /// </summary>
    /// <remarks>
    /// This was a plain static Dictionary, keyed per user per day and never emptied. Three
    /// problems, all of which only appear with users on the system.
    ///
    /// It grew without limit: one entry per active user per day, held until the process
    /// restarted. At ten thousand learners that is ten thousand objects a day that nothing ever
    /// removes.
    ///
    /// A Dictionary is not safe to write from several threads, and this is a web server. Two
    /// requests for two different users arriving together can corrupt its internal state - which
    /// does not throw tidily, it can spin a thread inside a lookup for ever.
    ///
    /// And it was per process, so a second instance would hold its own and the two would
    /// disagree about the same user's day. That alone makes the service impossible to run more
    /// than one copy of, which is the first thing anybody does to serve more people.
    /// </remarks>
    private static readonly TimeSpan FocusKeptFor = TimeSpan.FromHours(6);

    public MongoNavigatorService(RadarDatabase db, IOpportunityService opportunities, IMemoryCache cache)
    {
        _db           = db;
        _opportunities = opportunities;
        _cache        = cache;
    }

    private static string CacheKey(string userId) =>
        $"navigator-focus:{userId}:{DateTime.UtcNow:yyyy-MM-dd}";

    public async Task<NavigatorFocus> GetTodaysFocusAsync(UserProfile profile)
    {
        var key = CacheKey(profile.Id);

        if (_cache.TryGetValue(key, out NavigatorFocus? hit) && hit is not null) return hit;

        var focus = await BuildFocusAsync(profile);

        // Expiry rather than a size cap: a size cap only works if every writer in the process
        // declares one, and most here do not. Six hours bounds this to the people who actually
        // read Radar in a window, rather than to everyone who ever has.
        _cache.Set(key, focus, FocusKeptFor);

        return focus;
    }

    public Task<NavigatorFocus> RefreshAsync(UserProfile profile)
    {
        _cache.Remove(CacheKey(profile.Id));
        return GetTodaysFocusAsync(profile);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private async Task<NavigatorFocus> BuildFocusAsync(UserProfile profile)
    {
        var activeInterests = InterestPathService.ActiveInterests(profile);
        var layers = InterestLayerMapper.MapInterestsToLayers(activeInterests.Count > 0 ? activeInterests.ToList() : profile.Interests);

        var readTask   = PickBestItemAsync(profile, layers, ContentType.Article, ContentType.ResearchPaper);
        var watchTask  = PickBestItemAsync(profile, layers, ContentType.Video);
        var listenTask = PickBestItemAsync(profile, layers, ContentType.Podcast);
        var oppTask    = _opportunities.GetOpportunitiesAsync(profile, null, 1, 1);
        var learnTask  = BuildLearnCardAsync(profile);
        var buildTask  = BuildBuildCardAsync(profile);

        await Task.WhenAll(readTask, watchTask, listenTask, oppTask, learnTask, buildTask);

        var readItem   = readTask.Result;
        var watchItem  = watchTask.Result;
        var listenItem = listenTask.Result;
        var topOpp     = oppTask.Result.FirstOrDefault();
        var learnCard  = learnTask.Result;
        var buildCard  = buildTask.Result;

        return new NavigatorFocus
        {
            Learn  = learnCard,
            Read   = readItem   is not null ? ToReadCard(readItem)    : DefaultReadCard(),
            Watch  = watchItem  is not null ? ToWatchCard(watchItem)  : DefaultWatchCard(),
            Listen = listenItem is not null ? ToListenCard(listenItem) : DefaultListenCard(),
            Apply  = topOpp     is not null ? ToApplyCard(topOpp)     : DefaultApplyCard(),
            Build  = buildCard,
        };
    }

    // ── Content item selection ────────────────────────────────────────────────

    private async Task<ContentItem?> PickBestItemAsync(UserProfile profile, List<ContentLayer> layers, params ContentType[] types)
    {
        var typeFilter = Builders<ContentItem>.Filter.In(i => i.Type, types);

        // Prefer items in the user's interest layers, then fall back to anything
        if (layers.Count > 0)
        {
            var layerFilter = Builders<ContentItem>.Filter.In(i => i.Layer, layers);
            var items = await _db.ContentItems
                .Find(typeFilter & layerFilter)
                .SortBy(i => i.CredibilityTier)
                .ThenByDescending(i => i.PublishedAt)
                .Limit(20)
                .ToListAsync();
            var item = items.OrderByDescending(i => InterestPersonalization.ContentScore(i, profile)).FirstOrDefault();
            if (item is not null) return item;
        }

        return await _db.ContentItems
            .Find(typeFilter)
            .SortBy(i => i.CredibilityTier)
            .ThenByDescending(i => i.PublishedAt)
            .Limit(1)
            .FirstOrDefaultAsync();
    }

    // ── Card builders ────────────────────────────────────────────────────────

    private static NavigatorCard ToReadCard(ContentItem item) => new()
    {
        CardType      = NavigatorCardType.Read,
        ContentItemId = item.Id,
        Title         = item.Title,
        Subtitle      = item.Source,
        WhyItMatters  = !string.IsNullOrWhiteSpace(item.WhyItMatters) ? item.WhyItMatters : item.Signal,
        AiSummary     = item.AiSummary,
    };

    private static NavigatorCard ToWatchCard(ContentItem item) => new()
    {
        CardType      = NavigatorCardType.Watch,
        ContentItemId = item.Id,
        Title         = item.Title,
        Subtitle      = item.Source,
        EstimatedTime = item.EstimatedWatchTime ?? item.EstimatedReadTime,
        AiSummary     = item.AiSummary,
    };

    private static NavigatorCard ToListenCard(ContentItem item) => new()
    {
        CardType      = NavigatorCardType.Listen,
        ContentItemId = item.Id,
        Title         = item.Title,
        Subtitle      = item.Source,
        EstimatedTime = item.EstimatedReadTime,
        AiSummary     = !string.IsNullOrWhiteSpace(item.AiSummary) ? item.AiSummary : item.WhoShouldListen,
    };

    private static NavigatorCard ToApplyCard(Opportunity opp) => new()
    {
        CardType         = NavigatorCardType.Apply,
        OpportunityId    = opp.Id,
        Title            = opp.Title,
        Subtitle         = opp.Organisation,
        MatchScorePercent = opp.MatchScorePercent,
        DeadlineDays     = opp.DaysUntilDeadline == int.MaxValue ? null : (int?)opp.DaysUntilDeadline,
        ActionUrl        = opp.Url,
    };

    private async Task<NavigatorCard> BuildLearnCardAsync(UserProfile profile)
    {
        // 1. If user has an active roadmap, pick their actual next uncompleted lesson/module
        try
        {
            var activeRoadmap = await _db.Roadmaps
                .Find(r => r.UserId == profile.Id && r.IsActive)
                .FirstOrDefaultAsync();

            if (activeRoadmap is not null && activeRoadmap.Modules.Count > 0)
            {
                var currentModule = activeRoadmap.Modules.FirstOrDefault(m => !m.IsCompleted)
                    ?? activeRoadmap.Modules.LastOrDefault();

                if (currentModule is not null)
                {
                    var nextLesson = currentModule.Lessons.FirstOrDefault(l => !l.IsCompleted)
                        ?? currentModule.Lessons.FirstOrDefault();

                    if (nextLesson is not null)
                    {
                        var time = !string.IsNullOrWhiteSpace(nextLesson.EstimatedTime)
                            ? nextLesson.EstimatedTime
                            : "20 minutes";

                        return new NavigatorCard
                        {
                            CardType      = NavigatorCardType.Learn,
                            Title         = $"Master: {nextLesson.Title}",
                            Subtitle      = $"{activeRoadmap.Title} · {currentModule.Title}",
                            WhyItMatters  = $"Next priority lesson in your {activeRoadmap.Title} learning pathway.",
                            EstimatedTime = time,
                        };
                    }
                }
            }
        }
        catch
        {
            // Fall through to personalized profile-based recommendation
        }

        // 2. Derive specific skill or topic from user profile: Capabilities, Intent, Target Role, and Interests
        var primaryInterest = profile.DominantInterests.FirstOrDefault()
            ?? profile.Interests.FirstOrDefault()
        // Falls back to the interpreted field, not the raw goal: this becomes a topic to study,
        // and "Get Promotion" is not something anyone can study.
            ?? LearningIntentReader.Read(profile).Domain;

        // A) Explicit capability/skill the user listed
        var topCapability = profile.Capabilities.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        if (!string.IsNullOrWhiteSpace(topCapability))
        {
            var subtitle = !string.IsNullOrWhiteSpace(profile.TargetRole)
                ? $"Core competency for {profile.TargetRole}"
                : (!string.IsNullOrWhiteSpace(primaryInterest) ? $"{primaryInterest} Skill Track" : "Personalized Skill Track");

            return new NavigatorCard
            {
                CardType      = NavigatorCardType.Learn,
                Title         = $"Master: {topCapability.Trim()}",
                Subtitle      = subtitle,
                WhyItMatters  = $"Directly builds capability towards {(!string.IsNullOrWhiteSpace(profile.TargetRole) ? profile.TargetRole : profile.PrimaryGoal)}.",
                EstimatedTime = "25 minutes",
            };
        }

        // B) Current intent specified by user
        if (!string.IsNullOrWhiteSpace(profile.CurrentIntent))
        {
            return new NavigatorCard
            {
                CardType      = NavigatorCardType.Learn,
                Title         = $"Skill Focus: {profile.CurrentIntent.Trim()}",
                Subtitle      = !string.IsNullOrWhiteSpace(primaryInterest) ? $"{primaryInterest} Pathway" : "Targeted Learning Module",
                WhyItMatters  = $"Aligned with your current objective: {profile.CurrentIntent}.",
                EstimatedTime = "20 minutes",
            };
        }

        // C) Concrete skills mapped by interest or domain
        var specificTopic = MapInterestToConcreteSkill(primaryInterest, profile.TargetRole, profile.PrimaryGoal);
        return new NavigatorCard
        {
            CardType      = NavigatorCardType.Learn,
            Title         = specificTopic.Title,
            Subtitle      = specificTopic.Subtitle,
            WhyItMatters  = specificTopic.WhyItMatters,
            EstimatedTime = specificTopic.EstimatedTime,
        };
    }

    private async Task<NavigatorCard> BuildBuildCardAsync(UserProfile profile)
    {
        // 1. If user has an in-progress project in Project Studio, point to completing that exact artifact
        try
        {
            var inProgressProject = await _db.UserProjects
                .Find(p => p.UserId == profile.Id && !p.IsCompleted)
                .SortByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (inProgressProject is not null)
            {
                var deliverable = inProgressProject.Plan?.Deliverables.FirstOrDefault()
                    ?? inProgressProject.Description;

                var time = inProgressProject.Plan?.Milestones.FirstOrDefault(m => !m.IsComplete)?.EstimatedTime
                    ?? "45 minutes";

                return new NavigatorCard
                {
                    CardType      = NavigatorCardType.Build,
                    Title         = $"Complete: \"{inProgressProject.Title}\"",
                    Subtitle      = "Project Studio · In Progress",
                    WhyItMatters  = !string.IsNullOrWhiteSpace(deliverable)
                        ? $"Finish this deliverable ({deliverable}) and publish it to prove your skills in your career portfolio."
                        : "Finish and publish this project to showcase verified proof of your capabilities in your career portfolio.",
                    EstimatedTime = time,
                };
            }
        }
        catch
        {
            // Fall through to personalized recommendation
        }

        // 2. Check active roadmap for current module topic to align project with what they are learning
        string? roadmapContext = null;
        try
        {
            var activeRoadmap = await _db.Roadmaps
                .Find(r => r.UserId == profile.Id && r.IsActive)
                .FirstOrDefaultAsync();

            if (activeRoadmap is not null && activeRoadmap.Modules.Count > 0)
            {
                var currentModule = activeRoadmap.Modules.FirstOrDefault(m => !m.IsCompleted)
                    ?? activeRoadmap.Modules.FirstOrDefault();
                if (currentModule is not null)
                {
                    roadmapContext = $"{activeRoadmap.Title} · {currentModule.Title}";
                }
            }
        }
        catch { }

        // 3. Resolve exact concrete portfolio project artifact based on capabilities, target role, and interests
        var artifact = ResolveConcretePortfolioArtifact(profile, roadmapContext);
        return new NavigatorCard
        {
            CardType      = NavigatorCardType.Build,
            Title         = artifact.Title,
            Subtitle      = artifact.Subtitle,
            WhyItMatters  = artifact.WhyItMatters,
            EstimatedTime = artifact.EstimatedTime,
        };
    }

    // ── Fallback cards (no ingested data yet) ────────────────────────────────

    private static NavigatorCard DefaultReadCard() => new()
    {
        CardType     = NavigatorCardType.Read,
        Title        = "Your intelligence feed is filling up",
        Subtitle     = "Radar",
        WhyItMatters = "Content ingestion is running in the background. Check back in a few hours for personalised signals.",
        AiSummary    = "Configure your API keys to start receiving live signals.",
    };

    private static NavigatorCard DefaultWatchCard() => new()
    {
        CardType  = NavigatorCardType.Watch,
        Title     = "Video content will appear here",
        Subtitle  = "Coming soon",
        AiSummary = "Videos are sourced from your interest areas once ingestion runs.",
    };

    private static NavigatorCard DefaultListenCard() => new()
    {
        CardType  = NavigatorCardType.Listen,
        Title     = "Podcasts will appear here",
        Subtitle  = "Coming soon",
        AiSummary = "Podcast episodes are pulled from PodcastIndex and Taddy once credentials are set.",
    };

    private static NavigatorCard DefaultApplyCard() => new()
    {
        CardType          = NavigatorCardType.Apply,
        Title             = "Opportunities matched to your profile",
        Subtitle          = "Radar Opportunities",
        MatchScorePercent = 70,
        DeadlineDays      = 30,
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string Title, string Subtitle, string WhyItMatters, string EstimatedTime) MapInterestToConcreteSkill(
        string? interest, string? targetRole, string? primaryGoal)
    {
        var text = $"{interest} {targetRole} {primaryGoal}".ToLowerInvariant();

        if (text.Contains("fintech") || text.Contains("payment") || text.Contains("bank"))
            return (
                "Master: Payment Rails, APIs & Open Banking",
                "Fintech Market Infrastructure & Architecture",
                "Understand the core payment rails, settlement networks, and regulation driving African fintech.",
                "25 minutes"
            );

        if (text.Contains("venture") || text.Contains("invest") || text.Contains("equity") || text.Contains("capital"))
            return (
                "Master: Early-Stage Startup Valuation & Term Sheets",
                "Venture Capital & Investment Analysis",
                "Learn how investment rounds, liquidation preferences, and cap tables are evaluated in emerging markets.",
                "30 minutes"
            );

        if (text.Contains("ai") || text.Contains("machine learning") || text.Contains("data science") || text.Contains("deep learning") || text.Contains("model"))
            return (
                "Master: Practical LLM Prompting & Retrieval-Augmented Generation (RAG)",
                "AI Engineering & Applied Machine Learning",
                "Understand modern AI application architectures, context management, and vector search.",
                "25 minutes"
            );

        if (text.Contains("data") || text.Contains("analytics") || text.Contains("bi") || text.Contains("sql"))
            return (
                "Master: SQL Data Pipelines & Business Intelligence Metrics",
                "Data Analysis & Decision Modeling",
                "Turn raw business metrics into actionable cohorts, funnel models, and strategic insights.",
                "25 minutes"
            );

        if (text.Contains("product") || text.Contains("pm") || text.Contains("user experience") || text.Contains("ux"))
            return (
                "Master: Customer Discovery Interviews & Writing PRDs",
                "Product Strategy & User Research",
                "Learn the jobs-to-be-done framework and how to define crisp product requirements that engineering teams trust.",
                "20 minutes"
            );

        if (text.Contains("software") || text.Contains("engineer") || text.Contains("developer") || text.Contains("cloud") || text.Contains("system"))
            return (
                "Master: Event-Driven Architecture & Distributed Systems Design",
                "Software Engineering & Cloud Architecture",
                "High-scale system patterns for message queuing, idempotent APIs, and fault tolerance.",
                "30 minutes"
            );

        if (text.Contains("energy") || text.Contains("clean") || text.Contains("climate") || text.Contains("solar") || text.Contains("renew"))
            return (
                "Master: Decentralized Mini-Grid Economics & Solar Financing",
                "Clean Energy & Climate Infrastructure",
                "Explore unit economics and capital expenditure modeling for off-grid and renewable power installations.",
                "25 minutes"
            );

        if (text.Contains("agri") || text.Contains("farm") || text.Contains("food") || text.Contains("supply"))
            return (
                "Master: Smallholder Farmer Supply Chain Logistics & Commodity Indexing",
                "AgriTech & Food Security Systems",
                "Examine how digital platforms connect input financing, harvest logistics, and price transparency.",
                "20 minutes"
            );

        if (text.Contains("health") || text.Contains("medical") || text.Contains("biotech"))
            return (
                "Master: Telemedicine Adoption & Digital Health Record Interoperability",
                "HealthTech Systems & Clinical Workflows",
                "Learn how distributed health technology addresses provider bottlenecks and patient monitoring.",
                "25 minutes"
            );

        if (text.Contains("policy") || text.Contains("govern") || text.Contains("regulat") || text.Contains("trade"))
            return (
                "Master: Regulatory Sandbox Frameworks & Cross-Border Trade Treaties",
                "Public Policy & Economic Governance",
                "Discover how progressive regulatory policies facilitate commercial innovation while safeguarding stability.",
                "25 minutes"
            );

        if (text.Contains("entrepreneur") || text.Contains("startup") || text.Contains("business") || text.Contains("founder"))
            return (
                "Master: Unit Economics, Customer Acquisition (CAC) & LTV Modeling",
                "Startup Scaling & Business Model Design",
                "Model customer lifetime value and channel payback periods to ensure sustainable venture growth.",
                "25 minutes"
            );

        // Fallback using the exact interest name rather than a vague generic string
        var cleanInterest = !string.IsNullOrWhiteSpace(interest) ? interest.Trim() : (!string.IsNullOrWhiteSpace(primaryGoal) ? primaryGoal.Trim() : "Core Market Strategy");
        return (
            $"Master: Core Fundamentals & Frameworks in {cleanInterest}",
            $"{cleanInterest} Knowledge Track",
            $"Structured learning module tailored to sharpen your understanding of {cleanInterest}.",
            "20 minutes"
        );
    }

    private static (string Title, string Subtitle, string WhyItMatters, string EstimatedTime) ResolveConcretePortfolioArtifact(
        UserProfile profile, string? roadmapContext)
    {
        var primaryInterest = profile.DominantInterests.FirstOrDefault()
            ?? profile.Interests.FirstOrDefault()
        // Falls back to the interpreted field, not the raw goal: this becomes a topic to study,
        // and "Get Promotion" is not something anyone can study.
            ?? LearningIntentReader.Read(profile).Domain;

        var capability = profile.Capabilities.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        var targetRole = profile.TargetRole;
        var intent = profile.CurrentIntent;

        var text = $"{capability} {targetRole} {intent} {primaryInterest} {profile.PrimaryGoal}".ToLowerInvariant();

        if (text.Contains("fintech") || text.Contains("payment") || text.Contains("bank"))
            return (
                "Build for Portfolio: \"African Payment Gateway & Webhook Reconciliation Engine\"",
                roadmapContext ?? "Fintech Engineering · Working Systems Architecture",
                "Design and document an end-to-end payment state machine handling idempotency and settlement webhooks to prove your fintech systems capability.",
                "2–3 hours"
            );

        if (text.Contains("venture") || text.Contains("invest") || text.Contains("equity") || text.Contains("capital"))
            return (
                "Build for Portfolio: \"Series A Investment Memo & 3-Statement DCF Model\"",
                roadmapContext ?? "Venture Capital & Private Equity · Due Diligence Artifact",
                "Draft a structured 2-page investment thesis with unit economics and valuation multiples for an African tech startup to showcase analytical rigor.",
                "2–3 hours"
            );

        if (text.Contains("ai") || text.Contains("prompt") || text.Contains("rag") || text.Contains("llm") || text.Contains("machine learning"))
            return (
                "Build for Portfolio: \"Production RAG Assistant with Vector Search & Semantic Caching\"",
                roadmapContext ?? "Applied AI · Deployable GitHub Repository",
                "Ship a working retrieval-augmented generation app that demonstrates context window optimization and vector embeddings.",
                "3–4 hours"
            );

        if (text.Contains("data") || text.Contains("analytic") || text.Contains("sql") || text.Contains("bi"))
            return (
                "Build for Portfolio: \"Executive Cohort Retention & Churn Prediction Dashboard\"",
                roadmapContext ?? "Data Analytics · Interactive Dashboard & SQL Queries",
                "Model customer retention curves, LTV, and payback periods to prove you can turn complex transaction data into strategic decisions.",
                "2–3 hours"
            );

        if (text.Contains("product") || text.Contains("pm") || text.Contains("user experience") || text.Contains("ux"))
            return (
                "Build for Portfolio: \"Zero-to-One Product Requirement Document (PRD) & User Journey Map\"",
                roadmapContext ?? "Product Management · Strategic Case Study",
                "Document a verified customer problem statement, jobs-to-be-done, prioritization matrix, and feature specs that engineering teams can execute.",
                "2 hours"
            );

        if (text.Contains("software") || text.Contains("engineer") || text.Contains("developer") || text.Contains("cloud") || text.Contains("system"))
            return (
                "Build for Portfolio: \"Distributed Event-Driven Order Processing Microservice\"",
                roadmapContext ?? "Backend Engineering · Scalable System Architecture",
                "Demonstrate asynchronous message queues, database transactions, and automated test coverage in a public portfolio repository.",
                "3–4 hours"
            );

        if (text.Contains("energy") || text.Contains("clean") || text.Contains("climate") || text.Contains("solar") || text.Contains("renew"))
            return (
                "Build for Portfolio: \"Commercial Solar Mini-Grid CAPEX & LCOE Financial Model\"",
                roadmapContext ?? "Clean Energy & Climate · Project Finance Model",
                "Model levelized cost of electricity, battery degradation assumptions, and commercial tariffs for decentralized renewable power installations.",
                "2 hours"
            );

        if (text.Contains("agri") || text.Contains("farm") || text.Contains("food") || text.Contains("supply"))
            return (
                "Build for Portfolio: \"Smallholder Farmer Produce Aggregation & Cold-Chain Logistics Map\"",
                roadmapContext ?? "AgriTech · Supply Chain Architecture",
                "Design a field-tested operational workflow connecting farm-gate aggregation with digital warehouse receipts to prove supply chain execution.",
                "2 hours"
            );

        if (text.Contains("health") || text.Contains("medical") || text.Contains("biotech"))
            return (
                "Build for Portfolio: \"Interoperable Patient Triage & Telemedicine Routing Flowchart\"",
                roadmapContext ?? "HealthTech Systems · Clinical Workflow Architecture",
                "Specify an architectural blueprint solving clinic bottlenecks and automated follow-up scheduling for underserved communities.",
                "2 hours"
            );

        if (text.Contains("policy") || text.Contains("govern") || text.Contains("regulat") || text.Contains("trade"))
            return (
                "Build for Portfolio: \"Policy Brief: Regulatory Sandbox Guidelines for Digital Trade\"",
                roadmapContext ?? "Public Policy · Legislative Impact Brief",
                "Draft a 3-page actionable policy brief with stakeholder impact assessments and cross-border trade considerations.",
                "2 hours"
            );

        if (text.Contains("entrepreneur") || text.Contains("startup") || text.Contains("business") || text.Contains("founder"))
            return (
                "Build for Portfolio: \"Validated 1-Page Business Model Canvas & Pitch Deck Teaser\"",
                roadmapContext ?? "Venture Strategy · Investor-Ready Deliverable",
                "Outline verified customer pain points, channel unit economics, and competitive moats for investor due diligence.",
                "2 hours"
            );

        // Capability-specific fallback if user gave a capability
        if (!string.IsNullOrWhiteSpace(capability))
        {
            var cleanCap = capability.Trim();
            return (
                $"Build for Portfolio: \"{cleanCap} Implementation Case Study\"",
                roadmapContext ?? (!string.IsNullOrWhiteSpace(targetRole) ? $"{targetRole} Portfolio Artifact" : "Professional Portfolio Project"),
                $"Build a concrete, documented project applying {cleanCap} to solve a real market problem and showcase it in your portfolio.",
                "2–3 hours"
            );
        }

        // Domain-specific fallback using the exact interest
        var cleanInterest = !string.IsNullOrWhiteSpace(primaryInterest) ? primaryInterest.Trim() : "Market Strategy";
        return (
            $"Build for Portfolio: \"{cleanInterest} Strategic Analysis & Action Blueprint\"",
            roadmapContext ?? $"{cleanInterest} Portfolio Artifact",
            $"Create a documented, evidence-backed strategy artifact analyzing a key challenge in {cleanInterest} for your portfolio.",
            "2 hours"
        );
    }
}

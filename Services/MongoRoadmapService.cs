using System.Text;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoRoadmapService : IRoadmapService
{
    private readonly RadarDatabase _db;
    private readonly IAiEngine _ai;
    private readonly ILogger<MongoRoadmapService> _log;

    public MongoRoadmapService(RadarDatabase db, IAiEngine ai, ILogger<MongoRoadmapService> log)
    {
        _db = db;
        _ai = ai;
        _log = log;
    }

    public async Task<List<GrowthRoadmap>> GetUserRoadmapsAsync(string userId)
    {
        return await _db.Roadmaps
            .Find(r => r.UserId == userId)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<GrowthRoadmap?> GetActiveRoadmapAsync(string userId)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync();

        if (roadmap is not null) return roadmap;

        // No roadmap yet — look up the user's goal and auto-create from template
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();
        if (profile is null) return null;

        var primaryInterests = InterestPathService.ActiveInterests(profile);
        var context = InterestPersonalization.Contexts(profile).FirstOrDefault(c => primaryInterests.Contains(c.Interest, StringComparer.OrdinalIgnoreCase))
            ?? InterestPersonalization.Contexts(profile).FirstOrDefault();
        var path = profile.InterestPaths.FirstOrDefault(p => p.IsPrimary);
        var roadmapGoal = path is null ? profile.PrimaryGoal : $"{profile.PrimaryGoal} {path.Title}";
        var createdRoadmap = await CreateRoadmapAsync(userId, roadmapGoal);
        createdRoadmap.Goal = profile.PrimaryGoal;
        if (context is not null)
        {
            createdRoadmap.Interest = context.Interest;
            createdRoadmap.Level = context.Level;
            createdRoadmap.Interest = path?.Title ?? context.Interest;
            createdRoadmap.Title = $"{createdRoadmap.Interest}: {profile.PrimaryGoal}";
            await _db.Roadmaps.ReplaceOneAsync(r => r.Id == createdRoadmap.Id, createdRoadmap);
        }
        return createdRoadmap;
    }

    public async Task<GrowthRoadmap> CreateRoadmapAsync(string userId, string goal)
    {
        // Deactivate any existing active roadmap
        var deactivate = Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, false);
        await _db.Roadmaps.UpdateManyAsync(r => r.UserId == userId && r.IsActive, deactivate);

        var roadmap = BuildFromTemplate(userId, goal);
        await _db.Roadmaps.InsertOneAsync(roadmap);
        return roadmap;
    }

    public async Task<GrowthRoadmap> CreateAiRoadmapAsync(string userId, UserProfile profile, CancellationToken ct = default)
    {
        var context  = InterestPersonalization.Contexts(profile).FirstOrDefault();
        var path     = profile.InterestPaths.FirstOrDefault(p => p.IsPrimary);
        var interest = path?.Title ?? context?.Interest ?? profile.Interests.FirstOrDefault() ?? profile.PrimaryGoal;
        var level    = context?.Level ?? "Beginner";

        var modules = await BuildAiModulesAsync(profile, interest, level, ct);

        var roadmap = modules is { Count: > 0 }
            ? new GrowthRoadmap
            {
                UserId   = userId,
                Title    = $"{interest}: {profile.PrimaryGoal}",
                Goal     = profile.PrimaryGoal,
                Interest = interest,
                Level    = level,
                IsActive = true,
                Modules  = modules,
            }
            // Degrade to the deterministic template rather than handing back an empty pathway.
            : BuildFromTemplate(userId, profile.PrimaryGoal);

        roadmap.Interest = interest;
        roadmap.Level    = level;

        var deactivate = Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, false);
        await _db.Roadmaps.UpdateManyAsync(r => r.UserId == userId && r.IsActive, deactivate, cancellationToken: ct);
        await _db.Roadmaps.InsertOneAsync(roadmap, cancellationToken: ct);
        return roadmap;
    }

    /// <summary>
    /// Asks the shared AI engine for a pathway tailored to this profile. Returns null on any
    /// failure (no key, budget cap, unparseable JSON) so the caller can fall back to a template.
    /// </summary>
    private async Task<List<RoadmapModule>?> BuildAiModulesAsync(UserProfile profile, string interest, string level, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Build a learning pathway for this person on: {interest}");
            sb.AppendLine();
            sb.AppendLine("About them:");
            sb.AppendLine($"persona: {profile.Persona}");
            sb.AppendLine($"career goal: {profile.PrimaryGoal}");
            sb.AppendLine($"starting level: {level}");
            sb.AppendLine($"other interests: {string.Join(", ", profile.Interests.Where(i => !i.Equals(interest, StringComparison.OrdinalIgnoreCase)))}");
            if (profile.InterestContexts.Count > 0)
                sb.AppendLine($"their own words: {string.Join("; ", profile.InterestContexts.Select(c => $"{c.Interest} — {c.Goal}"))}");
            sb.AppendLine($"region: {profile.City}, {profile.Region}");
            sb.AppendLine();
            sb.AppendLine("Order the pathway so each step is only attempted once the previous one makes sense.");
            sb.AppendLine("Lessons must be things the person can actually do alone, and should reference the African context where it genuinely matters.");
            sb.AppendLine("Never invent facts about them, and do not reference any underlying AI provider or model.");
            sb.AppendLine("Return strict JSON with exactly this shape:");
            sb.AppendLine("{ \"modules\": [ { \"title\": \"...\", \"description\": \"why this stage matters\",");
            sb.AppendLine("  \"lessons\": [ { \"title\": \"...\", \"body\": \"one or two sentences on what to do\", \"estimatedTime\": \"e.g. 25 min\" } ] } ] }");
            sb.AppendLine("Give 4 to 6 modules with 3 to 5 lessons each.");

            var result = await _ai.GenerateJsonAsync<PathwayResult>(
                "learning-pathway",
                "You are Radar's learning architect. You design practical, ordered learning pathways for ambitious young Africans.",
                sb.ToString(),
                ct);

            var modules = result?.Modules
                ?.Where(m => !string.IsNullOrWhiteSpace(m.Title))
                .Select((m, index) => new RoadmapModule
                {
                    Title       = m.Title!,
                    Description = m.Description ?? string.Empty,
                    Order       = index + 1,
                    IsLocked    = index > 0,
                    Lessons     = (m.Lessons ?? [])
                        .Where(l => !string.IsNullOrWhiteSpace(l.Title))
                        .Select((l, lessonIndex) => new RoadmapLesson
                        {
                            Title         = l.Title!,
                            Body          = l.Body ?? string.Empty,
                            Order         = lessonIndex + 1,
                            EstimatedTime = l.EstimatedTime ?? string.Empty,
                        })
                        .ToList(),
                })
                .Where(m => m.Lessons.Count > 0)
                .ToList();

            return modules is { Count: > 0 } ? modules : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Learning pathway generation failed for user {UserId}", profile.Id);
            return null;
        }
    }

    private sealed class PathwayResult { public List<ModuleResult>? Modules { get; set; } }

    private sealed class ModuleResult
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<LessonResult>? Lessons { get; set; }
    }

    private sealed class LessonResult
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? EstimatedTime { get; set; }
    }

    public async Task CompleteLessonAsync(string userId, string roadmapId, string moduleId, string lessonId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is null) return;

        var module = roadmap.Modules.FirstOrDefault(m => m.Id == moduleId);
        var lesson = module?.Lessons.FirstOrDefault(l => l.Id == lessonId);
        if (lesson is null) return;

        lesson.IsCompleted = true;

        // Unlock next module when current is complete
        if (module!.Lessons.All(l => l.IsCompleted))
        {
            module.IsCompleted = true;
            var next = roadmap.Modules.FirstOrDefault(m => m.Order == module.Order + 1);
            if (next is not null) next.IsLocked = false;
        }

        // Recalculate progress
        var total = roadmap.Modules.Sum(m => m.Lessons.Count);
        var done  = roadmap.Modules.Sum(m => m.Lessons.Count(l => l.IsCompleted));
        roadmap.ProgressPercent = total > 0 ? (int)((double)done / total * 100) : 0;

        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    public async Task AddTopicToRoadmapAsync(string userId, string roadmapId, string topic)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is null) return;

        var newModule = new RoadmapModule
        {
            Title    = topic,
            Order    = roadmap.Modules.Count + 1,
            IsLocked = false,
            Lessons  =
            [
                new RoadmapLesson { Title = $"Introduction to {topic}", Order = 1, EstimatedTime = "20 min" },
                new RoadmapLesson { Title = $"Core concepts of {topic}", Order = 2, EstimatedTime = "30 min" },
                new RoadmapLesson { Title = $"Applying {topic} in practice", Order = 3, EstimatedTime = "25 min" },
            ]
        };
        roadmap.Modules.Add(newModule);
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    public async Task<int> GetProgressPercentAsync(string userId, string roadmapId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        return roadmap?.ProgressPercent ?? 0;
    }

    // ── Template builder ──────────────────────────────────────────────────────

    private static GrowthRoadmap BuildFromTemplate(string userId, string goal)
    {
        var normalized = goal.ToLowerInvariant();

        var modules = normalized switch
        {
            var g when g.Contains("product") => ProductManagementModules(),
            var g when g.Contains("data") || g.Contains("analytics") => DataScienceModules(),
            var g when g.Contains("entrepreneur") || g.Contains("startup") || g.Contains("business") => EntrepreneurshipModules(),
            var g when g.Contains("finance") || g.Contains("investment") || g.Contains("banking") => FinanceModules(),
            var g when g.Contains("software") || g.Contains("engineer") || g.Contains("developer") || g.Contains("coding") => SoftwareEngModules(),
            var g when g.Contains("policy") || g.Contains("government") || g.Contains("public") => PublicPolicyModules(),
            _ => GenericModules(goal),
        };

        return new GrowthRoadmap
        {
            UserId   = userId,
            Title    = goal,
            Goal     = goal,
            IsActive = true,
            Modules  = modules,
        };
    }

    private static List<RoadmapModule> ProductManagementModules() =>
    [
        Module(1, "PM Foundations", false, [
            Lesson(1, "What is Product Management?", "15 min"),
            Lesson(2, "The PM's role in a tech company", "20 min"),
            Lesson(3, "Product vs Project vs Program Management", "15 min"),
        ]),
        Module(2, "User Research", true, [
            Lesson(1, "Jobs-to-be-done framework", "20 min"),
            Lesson(2, "Conducting user interviews", "30 min"),
            Lesson(3, "Synthesising research insights", "25 min"),
            Lesson(4, "Building user personas", "20 min"),
        ]),
        Module(3, "Product Strategy", true, [
            Lesson(1, "Vision, mission, and strategy", "20 min"),
            Lesson(2, "Market sizing and opportunity", "25 min"),
            Lesson(3, "Competitive analysis frameworks", "20 min"),
        ]),
        Module(4, "Prioritisation", true, [
            Lesson(1, "RICE, MoSCoW, and ICE scoring", "20 min"),
            Lesson(2, "Building a product roadmap", "25 min"),
            Lesson(3, "Stakeholder alignment", "20 min"),
        ]),
        Module(5, "Metrics & Analytics", true, [
            Lesson(1, "North Star metrics", "20 min"),
            Lesson(2, "Defining success criteria", "20 min"),
            Lesson(3, "A/B testing fundamentals", "25 min"),
        ]),
        Module(6, "Execution", true, [
            Lesson(1, "Writing PRDs and user stories", "30 min"),
            Lesson(2, "Working with engineers and designers", "20 min"),
            Lesson(3, "Sprint planning and agile basics", "20 min"),
        ]),
        Module(7, "Interview Prep", true, [
            Lesson(1, "PM interview question types", "20 min"),
            Lesson(2, "Product design questions (practice)", "30 min"),
            Lesson(3, "Estimation questions (practice)", "25 min"),
            Lesson(4, "Strategy questions (practice)", "25 min"),
        ]),
    ];

    private static List<RoadmapModule> DataScienceModules() =>
    [
        Module(1, "Python & Data Foundations", false, [
            Lesson(1, "Python for data analysis", "30 min"),
            Lesson(2, "NumPy and Pandas essentials", "40 min"),
            Lesson(3, "Data cleaning and wrangling", "35 min"),
        ]),
        Module(2, "Statistics & Probability", true, [
            Lesson(1, "Descriptive statistics", "25 min"),
            Lesson(2, "Probability distributions", "30 min"),
            Lesson(3, "Hypothesis testing", "30 min"),
            Lesson(4, "Bayesian thinking basics", "25 min"),
        ]),
        Module(3, "Data Visualisation", true, [
            Lesson(1, "Matplotlib and Seaborn", "30 min"),
            Lesson(2, "Storytelling with data", "25 min"),
            Lesson(3, "Dashboard design principles", "20 min"),
        ]),
        Module(4, "Machine Learning", true, [
            Lesson(1, "Supervised learning fundamentals", "35 min"),
            Lesson(2, "Regression and classification", "35 min"),
            Lesson(3, "Decision trees and random forests", "30 min"),
            Lesson(4, "Model evaluation and validation", "30 min"),
        ]),
        Module(5, "Deep Learning", true, [
            Lesson(1, "Neural networks from scratch", "40 min"),
            Lesson(2, "Convolutional neural networks", "35 min"),
            Lesson(3, "Transformers and LLMs overview", "30 min"),
        ]),
        Module(6, "Real-World Projects", true, [
            Lesson(1, "End-to-end ML project workflow", "40 min"),
            Lesson(2, "Feature engineering techniques", "30 min"),
            Lesson(3, "Deploying a model with FastAPI", "35 min"),
        ]),
    ];

    private static List<RoadmapModule> EntrepreneurshipModules() =>
    [
        Module(1, "Entrepreneur Mindset", false, [
            Lesson(1, "Problem vs solution thinking", "20 min"),
            Lesson(2, "First-principles reasoning", "20 min"),
            Lesson(3, "Risk tolerance and resilience", "15 min"),
        ]),
        Module(2, "Idea Validation", true, [
            Lesson(1, "Customer discovery interviews", "30 min"),
            Lesson(2, "Building an MVP", "25 min"),
            Lesson(3, "Measuring product-market fit signals", "25 min"),
        ]),
        Module(3, "Business Models", true, [
            Lesson(1, "Revenue model options", "20 min"),
            Lesson(2, "Unit economics and CAC/LTV", "25 min"),
            Lesson(3, "Pricing strategy", "20 min"),
        ]),
        Module(4, "Fundraising", true, [
            Lesson(1, "Venture capital basics", "20 min"),
            Lesson(2, "Angel investors vs VCs", "20 min"),
            Lesson(3, "Pitching and the pitch deck", "30 min"),
            Lesson(4, "Term sheets and cap tables", "25 min"),
        ]),
        Module(5, "Growth & Marketing", true, [
            Lesson(1, "Growth loops vs funnels", "20 min"),
            Lesson(2, "Distribution channels in Africa", "25 min"),
            Lesson(3, "Content and community-led growth", "20 min"),
        ]),
        Module(6, "Operations & Team", true, [
            Lesson(1, "Hiring your first team members", "20 min"),
            Lesson(2, "Company culture foundations", "20 min"),
            Lesson(3, "Legal basics for startups", "25 min"),
        ]),
    ];

    private static List<RoadmapModule> FinanceModules() =>
    [
        Module(1, "Financial Foundations", false, [
            Lesson(1, "How financial markets work", "25 min"),
            Lesson(2, "Reading financial statements", "30 min"),
            Lesson(3, "Time value of money", "25 min"),
        ]),
        Module(2, "Investment Analysis", true, [
            Lesson(1, "Equity valuation methods", "30 min"),
            Lesson(2, "DCF modelling basics", "35 min"),
            Lesson(3, "Portfolio theory and diversification", "25 min"),
        ]),
        Module(3, "African Financial Markets", true, [
            Lesson(1, "Stock exchanges in Africa", "20 min"),
            Lesson(2, "Fintech landscape in Africa", "25 min"),
            Lesson(3, "Pan-African payment systems", "20 min"),
        ]),
        Module(4, "Corporate Finance", true, [
            Lesson(1, "Capital structure decisions", "25 min"),
            Lesson(2, "Mergers and acquisitions overview", "25 min"),
            Lesson(3, "Working capital management", "20 min"),
        ]),
        Module(5, "Macroeconomics & Policy", true, [
            Lesson(1, "Monetary policy and central banks", "25 min"),
            Lesson(2, "Inflation and currency dynamics in Africa", "25 min"),
            Lesson(3, "IMF and World Bank programmes", "20 min"),
        ]),
        Module(6, "Career Prep", true, [
            Lesson(1, "Investment banking interview prep", "30 min"),
            Lesson(2, "Financial modelling case study", "40 min"),
            Lesson(3, "Networking in finance", "20 min"),
        ]),
    ];

    private static List<RoadmapModule> SoftwareEngModules() =>
    [
        Module(1, "Computer Science Fundamentals", false, [
            Lesson(1, "Data structures: arrays, lists, trees", "35 min"),
            Lesson(2, "Algorithms and Big-O notation", "40 min"),
            Lesson(3, "Recursion and dynamic programming", "35 min"),
        ]),
        Module(2, "System Design", true, [
            Lesson(1, "Scalability principles", "30 min"),
            Lesson(2, "Databases: SQL vs NoSQL", "30 min"),
            Lesson(3, "Caching and CDNs", "25 min"),
            Lesson(4, "Microservices and monoliths", "25 min"),
        ]),
        Module(3, "Web & API Development", true, [
            Lesson(1, "RESTful API design", "30 min"),
            Lesson(2, "Authentication and authorisation", "25 min"),
            Lesson(3, "HTTP and networking basics", "25 min"),
        ]),
        Module(4, "DevOps & Cloud", true, [
            Lesson(1, "Docker and containerisation", "30 min"),
            Lesson(2, "CI/CD pipelines", "25 min"),
            Lesson(3, "Cloud providers: AWS, GCP, Azure overview", "25 min"),
        ]),
        Module(5, "Software Craft", true, [
            Lesson(1, "Clean code principles", "20 min"),
            Lesson(2, "Testing: unit, integration, E2E", "25 min"),
            Lesson(3, "Code reviews and collaboration", "20 min"),
        ]),
        Module(6, "Interview Prep", true, [
            Lesson(1, "LeetCode problem-solving patterns", "40 min"),
            Lesson(2, "Behavioural interview questions", "25 min"),
            Lesson(3, "System design mock interview", "40 min"),
        ]),
    ];

    private static List<RoadmapModule> PublicPolicyModules() =>
    [
        Module(1, "Policy Foundations", false, [
            Lesson(1, "How policy is made", "25 min"),
            Lesson(2, "Policy analysis frameworks", "25 min"),
            Lesson(3, "Stakeholder mapping", "20 min"),
        ]),
        Module(2, "African Governance", true, [
            Lesson(1, "AU and regional bodies", "25 min"),
            Lesson(2, "Federalism and decentralisation", "20 min"),
            Lesson(3, "Civil society and advocacy", "20 min"),
        ]),
        Module(3, "Economic Policy", true, [
            Lesson(1, "Trade and industrial policy", "25 min"),
            Lesson(2, "Social protection systems", "25 min"),
            Lesson(3, "Development finance institutions", "20 min"),
        ]),
        Module(4, "Research & Evidence", true, [
            Lesson(1, "Policy brief writing", "30 min"),
            Lesson(2, "Qualitative research methods", "25 min"),
            Lesson(3, "Using data to make the case", "25 min"),
        ]),
        Module(5, "Communication & Influence", true, [
            Lesson(1, "Policy communication for non-experts", "20 min"),
            Lesson(2, "Media engagement and op-eds", "20 min"),
            Lesson(3, "Parliamentary and legislative processes", "20 min"),
        ]),
        Module(6, "Career Pathways", true, [
            Lesson(1, "Working at government vs think tank vs NGO", "20 min"),
            Lesson(2, "Fellowship and graduate programs", "20 min"),
            Lesson(3, "Building your policy network", "15 min"),
        ]),
    ];

    private static List<RoadmapModule> GenericModules(string goal) =>
    [
        Module(1, "Foundations", false, [
            Lesson(1, $"Introduction to {goal}", "20 min"),
            Lesson(2, "Core concepts and terminology", "25 min"),
            Lesson(3, "Key frameworks and mental models", "20 min"),
        ]),
        Module(2, "Intermediate Skills", true, [
            Lesson(1, "Practical application", "30 min"),
            Lesson(2, "Common tools and resources", "25 min"),
            Lesson(3, "Case studies and examples", "25 min"),
        ]),
        Module(3, "Advanced Topics", true, [
            Lesson(1, "Expert-level concepts", "30 min"),
            Lesson(2, "Current research and trends", "25 min"),
            Lesson(3, "Building your own perspective", "20 min"),
        ]),
        Module(4, "Real-World Practice", true, [
            Lesson(1, "Project-based learning", "40 min"),
            Lesson(2, "Community and networking", "20 min"),
            Lesson(3, "Finding mentors and role models", "15 min"),
        ]),
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RoadmapModule Module(int order, string title, bool locked, List<RoadmapLesson> lessons) =>
        new() { Title = title, Order = order, IsLocked = locked, Lessons = lessons };

    private static RoadmapLesson Lesson(int order, string title, string time) =>
        new() { Title = title, Order = order, EstimatedTime = time };
}

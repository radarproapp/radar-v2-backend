using System.Text;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Manages project templates (seeded), user projects (persisted per-user), and the AI-generated
/// execution plan attached to a project.
/// </summary>
public class MongoProjectStudioService : IProjectStudioService
{
    private readonly RadarDatabase _db;
    private readonly IAiEngine _ai;
    private readonly ILogger<MongoProjectStudioService> _log;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoProjectStudioService(RadarDatabase db, IAiEngine ai, ILogger<MongoProjectStudioService> log)
    {
        _db = db;
        _ai = ai;
        _log = log;
    }

    public async Task<List<ProjectTemplate>> GetTemplatesAsync()
    {
        await EnsureSeededAsync();
        return await _db.ProjectTemplates.Find(_ => true)
            .SortBy(t => t.Category)
            .ThenBy(t => t.Title)
            .ToListAsync();
    }

    public async Task<List<StudioProject>> GetUserProjectsAsync(string userId)
    {
        return await _db.UserProjects
            .Find(p => p.UserId == userId)
            .SortByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<StudioProject> StartProjectAsync(string userId, string templateId)
    {
        var template = await _db.ProjectTemplates.Find(t => t.Id == templateId).FirstOrDefaultAsync();
        if (template is null)
            throw new InvalidOperationException("Template not found: " + templateId);

        var project = new StudioProject
        {
            TemplateId = templateId,
            Title = template.Title,
            Description = template.Description,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        await _db.UserProjects.InsertOneAsync(project);
        return project;
    }

    public async Task<StudioProject> CreateProjectAsync(string userId, string title, string description, string category)
    {
        var project = new StudioProject
        {
            TemplateId = $"custom:{category.ToLowerInvariant().Replace(' ', '-')}",
            Title = title.Trim(),
            Description = description.Trim(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        await _db.UserProjects.InsertOneAsync(project);
        return project;
    }

    public async Task<StudioProject?> GetProjectAsync(string userId, string projectId)
    {
        return await _db.UserProjects
            .Find(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<StudioProject?> GeneratePlanAsync(string userId, string projectId, UserProfile profile, CancellationToken ct = default)
    {
        var project = await _db.UserProjects
            .Find(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (project is null) return null;
        if (project.Plan is not null) return project;

        project.Plan = await BuildPlanAsync(profile, project, ct);
        await _db.UserProjects.ReplaceOneAsync(p => p.Id == projectId, project, cancellationToken: ct);
        return project;
    }

    // ── AI plan generation ────────────────────────────────────────────────────

    private async Task<ProjectPlan> BuildPlanAsync(UserProfile profile, StudioProject project, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Project: {project.Title}");
            sb.AppendLine($"What they want to produce: {project.Description}");
            if (!string.IsNullOrWhiteSpace(project.TemplateId)) sb.AppendLine($"Project type: {project.TemplateId}");
            sb.AppendLine();
            sb.AppendLine("About the person doing it:");
            sb.AppendLine($"persona: {profile.Persona}");
            sb.AppendLine($"career goal: {profile.PrimaryGoal}");
            sb.AppendLine($"interests: {string.Join(", ", profile.Interests)}");
            if (profile.InterestContexts.Count > 0)
                sb.AppendLine($"context: {string.Join("; ", profile.InterestContexts.Select(c => $"{c.Interest}: {c.Goal}"))}");
            sb.AppendLine($"region: {profile.City}, {profile.Region}");
            sb.AppendLine();
            sb.AppendLine("Write a realistic execution plan they can follow unaided over about a week.");
            sb.AppendLine("Ground every step in the specifics of this project — never generic advice like 'do research'.");
            sb.AppendLine("Do not invent facts about the person or claim any credentials.");
            sb.AppendLine("Return strict JSON with exactly this shape:");
            sb.AppendLine("{ \"summary\": \"One or two sentences on the approach\",");
            sb.AppendLine("  \"deliverables\": [\"the concrete things they will end up with\"],");
            sb.AppendLine("  \"successCriteria\": \"How they know it is done and good enough to show\",");
            sb.AppendLine("  \"milestones\": [ { \"title\": \"...\", \"outcome\": \"what exists after this\", \"estimatedTime\": \"e.g. 45 min\", \"steps\": [\"specific action\"] } ] }");
            sb.AppendLine("Give 4 to 6 milestones in dependency order, with 2 to 4 steps each.");

            var result = await _ai.GenerateJsonAsync<PlanResult>(
                "project-plan",
                "You are Radar's project coach. You turn vague ambitions into a short, concrete plan a person can start today. Be specific and never reference an underlying AI provider or model.",
                sb.ToString(),
                ct);

            if (result?.Milestones is { Count: > 0 })
            {
                return new ProjectPlan
                {
                    Summary        = result.Summary ?? string.Empty,
                    Deliverables   = result.Deliverables ?? [],
                    SuccessCriteria = result.SuccessCriteria,
                    Milestones     = result.Milestones
                        .Where(m => !string.IsNullOrWhiteSpace(m.Title))
                        .Select(m => new ProjectMilestone
                        {
                            Title         = m.Title!,
                            Outcome       = m.Outcome ?? string.Empty,
                            EstimatedTime = m.EstimatedTime ?? string.Empty,
                            Steps         = m.Steps ?? [],
                        })
                        .ToList(),
                };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Project plan generation failed for project {ProjectId}", project.Id);
        }

        return FallbackPlan(profile, project);
    }

    private sealed class PlanResult
    {
        public string? Summary { get; set; }
        public List<string>? Deliverables { get; set; }
        public string? SuccessCriteria { get; set; }
        public List<MilestoneResult>? Milestones { get; set; }
    }

    private sealed class MilestoneResult
    {
        public string? Title { get; set; }
        public string? Outcome { get; set; }
        public string? EstimatedTime { get; set; }
        public List<string>? Steps { get; set; }
    }

    /// <summary>
    /// Deterministic plan used when the AI engine is unavailable or budget-capped, so the feature
    /// degrades to something still useful instead of an error.
    /// </summary>
    private static ProjectPlan FallbackPlan(UserProfile profile, StudioProject project) => new()
    {
        Summary = $"Work through {project.Title} in four passes: scope it, gather what you need, build the thing, then tighten it for the audience you want to reach.",
        Deliverables =
        [
            project.Title,
            $"A short written summary connecting it to your goal: {profile.PrimaryGoal}",
        ],
        SuccessCriteria = "Someone unfamiliar with your work can understand what you did and why it matters without you explaining it.",
        Milestones =
        [
            new() { Title = "Scope it", Outcome = "A one-paragraph statement of what you are making and what you are leaving out.", EstimatedTime = "30 min", Steps = [$"Write down the outcome of {project.Title} in one sentence.", "List what is in scope and explicitly out of scope."] },
            new() { Title = "Gather inputs", Outcome = "The sources, data or examples you will actually use.", EstimatedTime = "60 min", Steps = ["Collect and skim the three most relevant sources.", "Save the strongest evidence with a note on why it matters."] },
            new() { Title = "Build the first version", Outcome = "A rough but complete version end to end.", EstimatedTime = "2–3 hours", Steps = ["Draft the whole thing before perfecting any part of it.", "Mark the three weakest spots rather than fixing them yet."] },
            new() { Title = "Tighten and share", Outcome = "A version you would be comfortable sending to someone.", EstimatedTime = "90 min", Steps = ["Fix the weakest spots you marked.", "Write two sentences on what you learned doing it."] },
        ],
    };

    public async Task CompleteProjectAsync(string userId, string projectId)
    {
        var project = await _db.UserProjects
            .Find(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync();

        if (project is null) return;

        project.IsCompleted = true;
        project.CompletedAt = DateTime.UtcNow;
        await _db.UserProjects.ReplaceOneAsync(p => p.Id == projectId, project);
    }

    public async Task SetVisibilityAsync(string userId, string projectId, bool isPublic)
    {
        var project = await _db.UserProjects
            .Find(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync();

        if (project is null) return;

        project.IsPublic = isPublic;
        await _db.UserProjects.ReplaceOneAsync(p => p.Id == projectId, project);
    }

    public async Task<StudioProject?> GetPublicProjectAsync(string projectId)
    {
        return await _db.UserProjects
            .Find(p => p.Id == projectId && p.IsPublic)
            .FirstOrDefaultAsync();
    }

    private async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            var count = await _db.ProjectTemplates.CountDocumentsAsync(Builders<ProjectTemplate>.Filter.Empty);
            if (count == 0)
            {
                await _db.ProjectTemplates.InsertManyAsync(Templates);
            }
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static readonly List<ProjectTemplate> Templates =
    [
        new() { Id = "t1", Title = "Product Case Study", Description = "Document a real product problem, your analysis, and proposed solution.", Category = "Product", EstimatedTime = "3–5 hours", Tags = ["Product", "Portfolio"] },
        new() { Id = "t2", Title = "Marketing Campaign", Description = "Plan a full campaign for a product or service including channels, messaging, and metrics.", Category = "Marketing", EstimatedTime = "4–6 hours", Tags = ["Marketing", "Strategy"] },
        new() { Id = "t3", Title = "Research Proposal", Description = "Write a formal research proposal including problem statement, methodology, and expected outcomes.", Category = "Research", EstimatedTime = "6–8 hours", Tags = ["Research", "Academic"] },
        new() { Id = "t4", Title = "Literature Review", Description = "Synthesise 10–15 papers on a topic into a structured academic review.", Category = "Research", EstimatedTime = "8–12 hours", Tags = ["Research", "Academic"] },
        new() { Id = "t5", Title = "Business Model Canvas", Description = "Map out the business model for a startup idea using the BMC framework.", Category = "Business", EstimatedTime = "2–3 hours", Tags = ["Entrepreneurship", "Strategy"] },
        new() { Id = "t6", Title = "Portfolio Project", Description = "Build a small technical project to demonstrate a skill from your roadmap.", Category = "Technical", EstimatedTime = "5–10 hours", Tags = ["Portfolio", "Technical"] },
    ];
}

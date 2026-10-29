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

    public MongoProjectStudioService(RadarDatabase db, IAiEngine ai, ILogger<MongoProjectStudioService> log)
    {
        _db = db;
        _ai = ai;
        _log = log;
    }

    public async Task<List<StageProject>> GetSuggestedProjectsAsync(string userId)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync();

        // Nothing suggested until there is a path to suggest from. A learner who has not said
        // what they are learning has no stage and no field, and the six templates that used to
        // fill this space were a catalogue rather than a suggestion - which is how somebody
        // studying mathematics came to be offered a Business Model Canvas.
        if (roadmap is null) return [];

        // The stage they are on first, then the one after it. Two is a choice; the whole path at
        // once is a backlog, and a project nobody has the skills for yet is discouraging rather
        // than aspirational.
        var ordered = roadmap.Modules
            .Where(m => m.Project is not null)
            .OrderBy(m => m.Order)
            .ToList();

        var current = ordered.FindIndex(m =>
            string.Equals(m.Level, roadmap.CurrentStage, StringComparison.OrdinalIgnoreCase));

        if (current < 0) current = 0;

        return ordered
            .Skip(current)
            .Take(SuggestedAtOnce)
            .Select(m => m.Project!)
            .ToList();
    }

    /// <summary>
    /// How many projects are offered at once.
    /// </summary>
    /// <remarks>
    /// Two: the stage they are on, and the one after it. One is an instruction rather than a
    /// choice, and a list is a backlog.
    /// </remarks>
    private const int SuggestedAtOnce = 2;

    public async Task<List<StudioProject>> GetUserProjectsAsync(string userId)
    {
        return await _db.UserProjects
            .Find(p => p.UserId == userId)
            .SortByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Starts one of the suggested projects, or returns the one already under way.
    ///
    /// Returns null for an id that is not among this learner's suggestions, rather than throwing:
    /// an unknown id used to surface as a 500 and the page simply failed with no explanation. And
    /// starting the same one twice returns the project already in progress, because it used to
    /// insert a second identical row every time the button was pressed.
    /// </summary>
    public async Task<StudioProject?> StartSuggestedAsync(string userId, string projectId)
    {
        var suggested = await GetSuggestedProjectsAsync(userId);
        var project = suggested.FirstOrDefault(p => p.Id == projectId);
        if (project is null) return null;

        var existing = await _db.UserProjects
            .Find(p => p.UserId == userId && p.TemplateId == projectId && !p.IsCompleted)
            .FirstOrDefaultAsync();

        if (existing is not null) return existing;

        var studioProject = new StudioProject
        {
            // The stage project it came from, so the standards it is judged against can be found
            // again when it is submitted.
            TemplateId = projectId,
            Title = project.Title,
            Description = project.Deliverable,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        await _db.UserProjects.InsertOneAsync(studioProject);
        return studioProject;
    }

    public async Task<StudioProject?> SubmitAsync(string userId, string projectId, ProjectSubmission submission)
    {
        var project = await _db.UserProjects
            .Find(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync();

        // Somebody else's project, or none. Null rather than an exception, so the controller can
        // say so in terms of this learner.
        if (project is null) return null;

        project.Submissions.Add(submission);

        await _db.UserProjects.UpdateOneAsync(
            p => p.Id == project.Id,
            Builders<StudioProject>.Update.Set(p => p.Submissions, project.Submissions));

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
}

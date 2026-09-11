using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Manages project templates (seeded) and user projects (persisted per-user).
/// </summary>
public class MongoProjectStudioService : IProjectStudioService
{
    private readonly RadarDatabase _db;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoProjectStudioService(RadarDatabase db) => _db = db;

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

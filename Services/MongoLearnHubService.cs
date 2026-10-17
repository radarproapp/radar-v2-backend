using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoLearnHubService : ILearnHubService
{
    private readonly RadarDatabase _db;
    private readonly IRoadmapService _roadmaps;

    public MongoLearnHubService(RadarDatabase db, IRoadmapService roadmaps)
    {
        _db = db;
        _roadmaps = roadmaps;
    }

    public async Task<LearnHubPayload> GetLearnHubAsync(string userId, string? subInterest = null, string? stage = null)
    {
        var roadmap = await _roadmaps.GetActiveRoadmapAsync(userId);
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();

        // Check if user has cybersecurity interest or goal
        var skillCandidate = $"{roadmap?.Interest} {roadmap?.Goal} {profile?.PrimaryGoal} {string.Join(" ", profile?.Interests ?? [])}";
        var skill = skillCandidate.Contains("cyber", StringComparison.OrdinalIgnoreCase) ||
                    skillCandidate.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                    skillCandidate.Contains("infosec", StringComparison.OrdinalIgnoreCase)
            ? "Cybersecurity"
            : !string.IsNullOrWhiteSpace(roadmap?.Interest)
            ? roadmap.Interest
            : !string.IsNullOrWhiteSpace(roadmap?.Goal)
            ? roadmap.Goal
            : "Cybersecurity";

        var currentStage = !string.IsNullOrWhiteSpace(stage)
            ? stage
            : roadmap?.CurrentStage ?? "Beginner";

        var payload = LearnHubCatalog.GetPayload(skill, currentStage, subInterest);

        if (roadmap is not null)
        {
            payload.CurrentStage = roadmap.CurrentStage ?? currentStage;
            payload.Goal = roadmap.Goal ?? payload.Goal;

            // Reconcile completed resources
            foreach (var res in payload.Resources)
            {
                if (roadmap.CompletedResourceIds.Contains(res.Id))
                {
                    res.IsCompleted = true;
                }
                if (roadmap.CompletedExerciseIds.Contains(res.Id))
                {
                    res.PracticeExercise = $"{res.PracticeExercise} [Completed ✓]";
                }
            }

            // Reconcile knowledge graph node status based on real activity
            foreach (var node in payload.GraphNodes)
            {
                if (roadmap.WeakAreas.Any(w => node.Label.Contains(w, StringComparison.OrdinalIgnoreCase) || w.Contains(node.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    node.Status = "WeakArea";
                    node.ConfidencePercent = Math.Max(25, node.ConfidencePercent - 20);
                }
                else if (roadmap.QuizResults.Any(q => (q.Topic.Contains(node.Label, StringComparison.OrdinalIgnoreCase) || node.Label.Contains(q.Topic, StringComparison.OrdinalIgnoreCase)) && q.Passed))
                {
                    node.Status = "Mastered";
                    node.ConfidencePercent = Math.Max(85, node.ConfidencePercent + 25);
                }
            }

            // Recalculate totals
            payload.TotalMasteredNodes = payload.GraphNodes.Count(n => n.Status == "Mastered");
            payload.TotalLearningNodes = payload.GraphNodes.Count(n => n.Status == "Learning");
            payload.TotalWeakNodes = payload.GraphNodes.Count(n => n.Status == "WeakArea");
        }

        return payload;
    }

    public async Task CompleteResourceAsync(string userId, string resourceId)
    {
        var roadmap = await _roadmaps.GetActiveRoadmapAsync(userId);
        if (roadmap is not null)
        {
            await _roadmaps.CompleteResourceAsync(userId, roadmap.Id, resourceId);
        }
    }

    public async Task ToggleSaveResourceAsync(string userId, string resourceId)
    {
        // Saved state can be tracked directly
        await Task.CompletedTask;
    }

    public async Task RecordNodeActivityAsync(string userId, NodeActivityRequest request)
    {
        var roadmap = await _roadmaps.GetActiveRoadmapAsync(userId);
        if (roadmap is null) return;

        if (request.ActivityType == "QuizFailed" || request.ScorePercent < 75)
        {
            if (!roadmap.WeakAreas.Contains(request.NodeId))
            {
                roadmap.WeakAreas.Add(request.NodeId);
            }
        }
        else if (request.ActivityType == "QuizPassed" || request.ScorePercent >= 75)
        {
            roadmap.WeakAreas.Remove(request.NodeId);
        }

        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmap.Id, roadmap);
    }
}

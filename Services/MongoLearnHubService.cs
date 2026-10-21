using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoLearnHubService : ILearnHubService
{
    private readonly RadarDatabase _db;
    private readonly IRoadmapService _roadmaps;
    private readonly IAiEngine _ai;
    private readonly ILogger<MongoLearnHubService> _log;

    public MongoLearnHubService(RadarDatabase db, IRoadmapService roadmaps, IAiEngine ai, ILogger<MongoLearnHubService> log)
    {
        _db = db;
        _roadmaps = roadmaps;
        _ai = ai;
        _log = log;
    }

    public async Task<LearnHubPayload> GetLearnHubAsync(string userId, string? subInterest = null, string? stage = null)
    {
        var roadmap = await _roadmaps.GetActiveRoadmapAsync(userId);
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();

        // Check if user has cybersecurity interest or goal
        // Includes the interpreted field so an outcome goal still routes to the right domain.
        var interpretedDomain = profile is not null ? LearningIntentReader.Read(profile).Domain : "";
        var skillCandidate = $"{roadmap?.Interest} {roadmap?.Goal} {interpretedDomain} {string.Join(" ", profile?.Interests ?? [])}";
        // The learner's own words, in the order they stated them most firmly. The last rung used to
        // be "Cybersecurity", so a learner with no roadmap and no stated goal — a brand new user —
        // was handed a cybersecurity hub and nothing told them why. Their interests and interpreted
        // domain are consulted first now, so that default is reached only when Radar genuinely
        // knows nothing at all about them.
        var skill = Firm(roadmap?.Interest)
                    ?? Firm(roadmap?.Goal)
                    ?? Firm(interpretedDomain)
                    ?? Firm(profile?.Interests.FirstOrDefault())
                    ?? Firm(profile?.PrimaryGoal)
                    ?? string.Empty;

        var currentStage = !string.IsNullOrWhiteSpace(stage)
            ? stage
            : roadmap?.CurrentStage ?? "Beginner";

        LearnHubPayload payload;
        var normalizedSkill = skill.ToLowerInvariant();
        // Which fields the hand-written catalogue actually covers. Everything else goes to the
        // AI path, which builds from the learner's own subject.
        if (DomainVocabulary.FieldOf(normalizedSkill) is not null || normalizedSkill.Contains("machine learning"))
        {
            payload = LearnHubCatalog.GetPayload(skill, currentStage, subInterest);
        }
        else
        {
            payload = await SynthesizeAiLearnHubAsync(skill, currentStage, subInterest, profile, roadmap)
                      ?? LearnHubCatalog.GetPayload(skill, currentStage, subInterest);
        }

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
                    res.PracticeExercise = $"{res.PracticeExercise} [Completed]";
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

            ScopeToActivePath(payload, roadmap);
            SetNextStep(payload, roadmap);
        }

        return payload;
    }

    /// <summary>
    /// Narrows the hub to what this learner is actually working on.
    ///
    /// Spec 6.1. The sub-interests came from whatever the catalogue listed, which meant every
    /// learner on a given field saw the same three regardless of where they had got to. Taking
    /// them from the current stage's own modules keeps the hub the width of one person's path.
    ///
    /// Capped at three. A list of everything is the same as no list - the point of narrowing is
    /// that something is left out.
    /// </summary>
    private static void ScopeToActivePath(LearnHubPayload payload, GrowthRoadmap roadmap)
    {
        var stage = roadmap.CurrentStage ?? "Beginner";

        var subInterests = roadmap.Modules
            .Where(m => string.Equals(m.Level, stage, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (subInterests.Count == 0) return;

        payload.SubInterests = subInterests;
        if (string.IsNullOrWhiteSpace(payload.ActiveSubInterest))
        {
            payload.ActiveSubInterest = subInterests[0];
        }
    }

    /// <summary>
    /// The one thing to do next, named.
    ///
    /// Spec 6.5. The hub carried whatever next action the catalogue had been written with, which
    /// is a suggestion about the subject rather than about this learner - it did not move when
    /// they finished something. The first lesson they have not completed is a fact about their
    /// own progress, and it is the answer to the question the screen is asking.
    ///
    /// Leaves the existing action alone when everything is complete, rather than inventing a next
    /// step past the end of the path.
    /// </summary>
    private static void SetNextStep(LearnHubPayload payload, GrowthRoadmap roadmap)
    {
        var stage = roadmap.CurrentStage ?? "Beginner";

        var next = roadmap.Modules
            .Where(m => string.Equals(m.Level, stage, StringComparison.OrdinalIgnoreCase))
            .SelectMany(m => m.Lessons.OrderBy(l => l.Order).Select(l => new { Module = m, Lesson = l }))
            .FirstOrDefault(x => !x.Lesson.IsCompleted);

        // Nothing unfinished at this stage: say so rather than pointing at a lesson they have done.
        if (next is null) return;

        payload.NextAction = new NextBestAction
        {
            Title = next.Lesson.Title,
            Description = string.IsNullOrWhiteSpace(next.Module.Title)
                ? "The next lesson you have not finished."
                : $"Next in {next.Module.Title}.",
            WhyNowReason = $"It is the first thing you have not finished in your {stage.ToLowerInvariant()} stage.",
            EstimatedTime = next.Lesson.EstimatedTime ?? string.Empty,
            Stage = stage,
            SubInterest = next.Module.Title ?? string.Empty,
        };
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

    private async Task<LearnHubPayload?> SynthesizeAiLearnHubAsync(
        string skill,
        string stage,
        string? subInterest,
        UserProfile? profile,
        GrowthRoadmap? roadmap)
    {
        try
        {
            var prompt = $$"""
                Synthesize a comprehensive Radar Learn Hub payload for:
                - Primary Skill/Topic: {{skill}}
                - Current Progression Stage: {{stage}}
                - Active Sub-Interest filter: {{subInterest ?? "All"}}
                - User Goal: {{profile?.PrimaryGoal ?? skill}}
                - Target Role: {{profile?.TargetRole ?? "Professional"}}
                - Weak Areas: {{string.Join(", ", roadmap?.WeakAreas ?? [])}}

                Requirements:
                1. 1-3 highly relevant sub-interests.
                2. A connected Knowledge Graph (Goal -> Skills -> Topics -> Projects -> Opportunities) with at least 6 nodes.
                3. A rich mix of learning resources (~80% tutorial/how-to with step-by-step breakdown & practical exercise; ~20% latest developments with why it matters/your edge/next move).
                4. Include at least 4 resources across Tutorial Videos, Technical Articles, Podcasts, and Academic Papers.

                Return strictly formatted JSON matching the LearnHubPayload schema:
                {
                  "skill": "{{skill}}",
                  "goal": "Master {{skill}} to achieve target outcomes",
                  "currentStage": "{{stage}}",
                  "activeSubInterest": "{{subInterest ?? "All Sub-Interests"}}",
                  "subInterests": ["Foundations of {{skill}}", "Advanced {{skill}} Architecture", "Production & Real-World {{skill}}"],
                  "graphNodes": [
                    {
                      "id": "goal-node",
                      "label": "Goal: {{profile?.PrimaryGoal ?? skill}}",
                      "category": "Goal",
                      "status": "Learning",
                      "confidencePercent": 40,
                      "summary": "Target professional milestone",
                      "connectedNodeIds": ["skill-core"]
                    },
                    {
                      "id": "skill-core",
                      "label": "{{skill}} Fundamentals",
                      "category": "Skill",
                      "status": "Learning",
                      "confidencePercent": 50,
                      "summary": "Core syntax, paradigms, and architecture",
                      "connectedNodeIds": ["topic-deep"]
                    },
                    {
                      "id": "topic-deep",
                      "label": "Applied {{skill}} Implementation",
                      "category": "Topic",
                      "status": "Learning",
                      "confidencePercent": 35,
                      "summary": "Real-world engineering patterns and problem solving",
                      "connectedNodeIds": ["project-capstone"]
                    },
                    {
                      "id": "project-capstone",
                      "label": "Production Capstone System",
                      "category": "Project",
                      "status": "Learning",
                      "confidencePercent": 25,
                      "summary": "End-to-end portfolio proof-of-concept",
                      "connectedNodeIds": ["opp-target"]
                    },
                    {
                      "id": "opp-target",
                      "label": "Target Role Opportunity",
                      "category": "Opportunity",
                      "status": "Learning",
                      "confidencePercent": 20,
                      "summary": "Industry placement and strategic roles",
                      "connectedNodeIds": []
                    }
                  ],
                  "resources": [
                    {
                      "id": "res-1",
                      "title": "Mastering {{skill}} from Ground Up",
                      "source": "Industry Masterclass",
                      "type": "Video",
                      "format": "Tutorial",
                      "level": "{{stage}}",
                      "duration": "18 mins",
                      "url": "https://www.youtube.com/results?search_query=Mastering+{{skill}}+tutorial",
                      "summary": "Step-by-step technical walkthrough demonstrating core architecture and configuration.",
                      "stepByStepBreakdown": [
                        "1. Setting up the environment and isolating prerequisites",
                        "2. Implementing the core workflow with deterministic rules",
                        "3. Running sanity tests and verifying output logs"
                      ],
                      "practiceExercise": "Deploy a local instance and run 3 test scenarios in under 20 minutes.",
                      "tags": ["{{skill}}", "Tutorial", "How-To"]
                    },
                    {
                      "id": "res-2",
                      "title": "Critical Developments and Emerging Trends in {{skill}}",
                      "source": "Technology Review",
                      "type": "Article",
                      "format": "Development",
                      "level": "{{stage}}",
                      "duration": "7 mins read",
                      "url": "https://radar.app",
                      "summary": "Analysis of new industry benchmarks and operational paradigms.",
                      "whyItMatters": "Shifts standard operational expectations and creates demand for engineers with hands-on validation experience.",
                      "yourEdge": "Adopting these patterns early positions you ahead of teams relying on deprecated tooling.",
                      "nextMove": "Audit your current project configurations against the recommended architecture.",
                      "tags": ["{{skill}}", "Trends", "Analysis"]
                    }
                  ]
                }
                """;

            // Cache-first: the Learn Hub always has a catalog fallback, so the page should render from
            // it immediately and pick up the synthesised hub on a later visit rather than holding the
            // first load open on a provider round trip. The prompt is stable per skill/stage, so the
            // warm almost always lands before the user comes back.
            const string feature = "learnhub-synthesis";
            // The worked example above used to carry a real-looking watch?v= id, which taught the
            // model that inventing video ids was the expected output. It was also dead: of the 33
            // hardcoded ids in this codebase, 21 returned 404. A model cannot know whether a video
            // exists, so it is told to link to a search instead of guessing an id.
            const string system =
                "You are Radar's AI Learn Hub Architect. Create hyper-targeted, structured learning hubs and knowledge graphs. "
                + "Never invent a YouTube video id or a direct watch?v= link - you cannot know whether one exists, and a dead "
                + "link is worse than no link. For videos, return a YouTube search URL of the form "
                + "https://www.youtube.com/results?search_query=<terms> so the learner lands on real current results.";

            if (!_ai.TryGetCachedJson<LearnHubPayload>(feature, system, prompt, out var payload) || payload is null)
            {
                _ai.WarmJson<LearnHubPayload>(feature, system, prompt);
                return null;
            }

            if (payload != null)
            {
                payload.TotalMasteredNodes = payload.GraphNodes.Count(n => n.Status == "Mastered");
                payload.TotalLearningNodes = payload.GraphNodes.Count(n => n.Status == "Learning");
                payload.TotalWeakNodes = payload.GraphNodes.Count(n => n.Status == "WeakArea");
                return payload;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Non-fatal: AI Learn Hub synthesis failed for {Skill}; using catalog fallback", skill);
        }

        return null;
    }

    private static string? Firm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

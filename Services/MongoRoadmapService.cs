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
    private readonly IUserLearningRecordService _records;
    private readonly YouTubeSearchService _youtube;
    private readonly ILogger<MongoRoadmapService> _log;

    public MongoRoadmapService(
        RadarDatabase db,
        IAiEngine ai,
        IUserLearningRecordService records,
        YouTubeSearchService youtube,
        ILogger<MongoRoadmapService> log)
    {
        _db = db;
        _ai = ai;
        _records = records;
        _youtube = youtube;
        _log = log;
    }

    /// <summary>
    /// Re-tailors a roadmap against everything Radar currently knows about the user. Runs on every
    /// read, not just at creation, because the evidence keeps arriving: a topic failed twice should
    /// resurface, and a stage already proven should not hold someone back.
    /// </summary>
    private async Task PersonalizeAsync(GrowthRoadmap roadmap, string userId, CancellationToken ct = default)
    {
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync(ct);
        if (profile is null) return;

        var record = await _records.BuildAsync(profile, ct);
        RoadmapPersonalizer.Apply(roadmap, record);
    }


    /// <summary>
    /// Replaces a roadmap's modules when the template that should build them has changed, keeping
    /// everything the user earned: completed lessons are carried across by title, so a rebuild
    /// never costs someone their progress.
    /// </summary>
    private async Task<GrowthRoadmap> RebuildIfStaleAsync(GrowthRoadmap roadmap, string userId)
    {
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();
        if (profile is null) return roadmap;

        var goal = !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : profile.PrimaryGoal;
        var intent = LearningIntentReader.Read(profile);
        var expected = TemplateKeyFor(goal, intent);

        if (string.Equals(roadmap.TemplateKey, expected, StringComparison.Ordinal)) return roadmap;

        var previous = roadmap.TemplateKey ?? "(none)";
        var rebuilt = BuildFromTemplate(userId, goal, profile.PrimaryGoal, intent);

        // Carry progress across by lesson title, which survives a template change better than an id.
        var completedTitles = roadmap.Modules
            .SelectMany(m => m.Lessons)
            .Where(l => l.IsCompleted)
            .Select(l => l.Title)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var lesson in rebuilt.Modules.SelectMany(m => m.Lessons))
            lesson.IsCompleted = completedTitles.Contains(lesson.Title);

        roadmap.Modules = rebuilt.Modules;
        roadmap.TemplateKey = rebuilt.TemplateKey;
        roadmap.Title = rebuilt.Title;
        if (!string.IsNullOrWhiteSpace(rebuilt.Interest)) roadmap.Interest = rebuilt.Interest;

        _log.LogInformation("Rebuilt roadmap {RoadmapId}: template {Old} -> {New}.", roadmap.Id, previous, expected);
        return roadmap;
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

        if (roadmap is not null)
        {
            // Rebuild the modules when the stored roadmap came from a template this goal would no
            // longer choose. Without this, a path generated before the goal interpreter existed
            // keeps its old titles forever — the read path only ever re-tailored stages, so a user
            // whose goal is "Get Promotion" went on seeing "Foundations of Get Promotion".
            roadmap = await RebuildIfStaleAsync(roadmap, userId);

            // Re-enrich with latest catalog structure (4 stages, ordered resources, practical exercises)
            await EnrichRoadmapWithResourcesAsync(roadmap);
            await PersonalizeAsync(roadmap, userId);
            await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmap.Id, roadmap);
            return roadmap;
        }

        // No roadmap yet — look up the user's goal and auto-create from template
        var profile = await _db.Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync();
        if (profile is null) return null;

        var primaryInterests = InterestPathService.ActiveInterests(profile);
        var context = InterestPersonalization.Contexts(profile).FirstOrDefault(c => primaryInterests.Contains(c.Interest, StringComparer.OrdinalIgnoreCase))
            ?? InterestPersonalization.Contexts(profile).FirstOrDefault();
        var path = profile.InterestPaths.FirstOrDefault(p => p.IsPrimary);
        var roadmapGoal = path is null ? profile.PrimaryGoal : $"{profile.PrimaryGoal} {path.Title}";
        var createdRoadmap = await CreateRoadmapAsync(userId, roadmapGoal, profile.PrimaryGoal, LearningIntentReader.Read(profile));
        createdRoadmap.Goal = profile.PrimaryGoal;
        if (context is not null)
        {
            createdRoadmap.Interest = context.Interest;
            createdRoadmap.Level = context.Level;
            createdRoadmap.Interest = path?.Title ?? context.Interest;
            // Uses the interpreted subject, not the raw goal, so an outcome goal does not come back
            // as "Cybersecurity: Get Promotion" after BuildFromTemplate already phrased it properly.
            var titleIntent = LearningIntentReader.Read(profile);
            createdRoadmap.Title = titleIntent.IsOutcomeDriven
                ? Capitalise(titleIntent.Subject)
                : $"{createdRoadmap.Interest}: {profile.PrimaryGoal}";
            await EnrichRoadmapWithResourcesAsync(createdRoadmap);

            if (profile.Persona == PersonaType.Student && profile.PersonaDetails.TryGetValue("difficultCourses", out var diffCourses) && !string.IsNullOrWhiteSpace(diffCourses))
            {
                var split = diffCourses.Split(new[] { ',', ';', '/', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var d in split)
                {
                    if (!createdRoadmap.WeakAreas.Contains(d))
                        createdRoadmap.WeakAreas.Add(d);
                }
            }

            await _db.Roadmaps.ReplaceOneAsync(r => r.Id == createdRoadmap.Id, createdRoadmap);
        }
        return createdRoadmap;
    }

    public Task<GrowthRoadmap> CreateRoadmapAsync(string userId, string goal) =>
        CreateRoadmapAsync(userId, goal, null, null);

    public async Task<GrowthRoadmap> CreateRoadmapAsync(
        string userId, string goal, string? displayGoal, LearningIntent? intent = null)
    {
        // Deactivate any existing active roadmap
        var deactivate = Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, false);
        await _db.Roadmaps.UpdateManyAsync(r => r.UserId == userId && r.IsActive, deactivate);

        var roadmap = BuildFromTemplate(userId, goal, displayGoal, intent);
        await EnrichRoadmapWithResourcesAsync(roadmap);
        await PersonalizeAsync(roadmap, userId);
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
        await EnrichRoadmapWithResourcesAsync(roadmap);
        await PersonalizeAsync(roadmap, userId, ct);

        var deactivate = Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, false);
        await _db.Roadmaps.UpdateManyAsync(r => r.UserId == userId && r.IsActive, deactivate, cancellationToken: ct);
        await _db.Roadmaps.InsertOneAsync(roadmap, cancellationToken: ct);
        return roadmap;
    }

    private async Task<List<RoadmapModule>?> BuildAiModulesAsync(UserProfile profile, string interest, string level, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Build a 4-stage learning pathway (Beginner → Intermediate → Advanced → Real-world Application) for: {interest}");
            sb.AppendLine();
            sb.AppendLine("About them:");
            sb.AppendLine($"persona: {profile.Persona}");
            sb.AppendLine($"career goal: {profile.PrimaryGoal}");
            sb.AppendLine($"starting level: {level}");
            sb.AppendLine($"other interests: {string.Join(", ", profile.Interests.Where(i => !i.Equals(interest, StringComparison.OrdinalIgnoreCase)))}");
            sb.AppendLine();
            sb.AppendLine("Return exactly 4 modules representing the 4 stages:");
            sb.AppendLine("Stage 1: Beginner (Foundations, core tools)");
            sb.AppendLine("Stage 2: Intermediate (Applied workflows, modelling)");
            sb.AppendLine("Stage 3: Advanced (Scale, prediction, optimization)");
            sb.AppendLine("Stage 4: Application (Real-world projects, portfolio, case studies, interview prep)");
            sb.AppendLine("Return strict JSON with exactly this shape:");
            sb.AppendLine("{ \"modules\": [ { \"title\": \"...\", \"description\": \"...\", \"stageGoal\": \"...\",");
            sb.AppendLine("  \"lessons\": [ { \"title\": \"...\", \"body\": \"...\", \"estimatedTime\": \"e.g. 25 min\" } ] } ] }");

            var result = await _ai.GenerateJsonAsync<PathwayResult>(
                "learning-pathway",
                "You are Radar's learning architect. You design practical, ordered learning pathways with a 4-stage progression from Beginner to Application.",
                sb.ToString(),
                ct);

            var stages = new[] { "Beginner", "Intermediate", "Advanced", "Application" };
            var modules = result?.Modules
                ?.Where(m => !string.IsNullOrWhiteSpace(m.Title))
                .Select((m, index) => new RoadmapModule
                {
                    Title       = m.Title!,
                    Description = m.Description ?? string.Empty,
                    StageGoal   = m.StageGoal ?? string.Empty,
                    Order       = index + 1,
                    Level       = index < stages.Length ? stages[index] : stages[3],
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
        public string? StageGoal { get; set; }
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

        if (module!.Lessons.All(l => l.IsCompleted))
        {
            module.IsCompleted = true;
            var next = roadmap.Modules.FirstOrDefault(m => m.Order == module.Order + 1);
            if (next is not null) next.IsLocked = false;
        }

        RecalculateMastery(roadmap);
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    public async Task CompleteResourceAsync(string userId, string roadmapId, string resourceId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is null) return;

        if (roadmap.CompletedResourceIds.Contains(resourceId))
            roadmap.CompletedResourceIds.Remove(resourceId);
        else
            roadmap.CompletedResourceIds.Add(resourceId);

        var isCompleted = roadmap.CompletedResourceIds.Contains(resourceId);

        var res = roadmap.Resources.FirstOrDefault(r => r.Id == resourceId);
        if (res is not null) res.IsCompleted = isCompleted;

        foreach (var mod in roadmap.Modules)
        {
            var modRes = mod.Resources.FirstOrDefault(r => r.Id == resourceId);
            if (modRes is not null) modRes.IsCompleted = isCompleted;
        }

        RecalculateMastery(roadmap);
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    public async Task CompleteExerciseAsync(string userId, string roadmapId, string resourceId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is null) return;

        if (roadmap.CompletedExerciseIds.Contains(resourceId))
            roadmap.CompletedExerciseIds.Remove(resourceId);
        else
            roadmap.CompletedExerciseIds.Add(resourceId);

        var isCompleted = roadmap.CompletedExerciseIds.Contains(resourceId);

        var res = roadmap.Resources.FirstOrDefault(r => r.Id == resourceId);
        if (res is not null) res.IsExerciseCompleted = isCompleted;

        foreach (var mod in roadmap.Modules)
        {
            var modRes = mod.Resources.FirstOrDefault(r => r.Id == resourceId);
            if (modRes is not null) modRes.IsExerciseCompleted = isCompleted;
        }

        RecalculateMastery(roadmap);
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    public async Task<GrowthRoadmap?> RecordQuizResultAsync(string userId, string roadmapId, string topic, string stage, int scorePercent)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is null) return null;

        var record = new QuizPerformanceRecord
        {
            Topic = topic,
            Stage = stage,
            ScorePercent = scorePercent,
            TakenAt = DateTime.UtcNow
        };
        roadmap.QuizResults.Add(record);

        if (scorePercent < 75)
        {
            if (!roadmap.WeakAreas.Contains(topic)) roadmap.WeakAreas.Add(topic);
            roadmap.AdaptiveRecommendation = $"Reinforce {topic}: your quiz score was {scorePercent}%. Review the tutorial breakdown and retry the practice task before advancing.";
        }
        else
        {
            roadmap.WeakAreas.Remove(topic);
            roadmap.AdaptiveRecommendation = $"Mastery demonstrated in {topic} ({scorePercent}%)! Great work—continue to the next milestone in {stage}.";
        }

        RecalculateMastery(roadmap);
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
        return roadmap;
    }

    public async Task SetRoadmapStageAsync(string userId, string roadmapId, string stage)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is null) return;

        roadmap.CurrentStage = stage;
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
            Level    = "Intermediate",
            IsLocked = false,
            Lessons  =
            [
                new RoadmapLesson { Title = $"Introduction to {topic}", Order = 1, EstimatedTime = "20 min" },
                new RoadmapLesson { Title = $"Core concepts of {topic}", Order = 2, EstimatedTime = "30 min" },
                new RoadmapLesson { Title = $"Applying {topic} in practice", Order = 3, EstimatedTime = "25 min" },
            ]
        };
        roadmap.Modules.Add(newModule);
        await EnrichRoadmapWithResourcesAsync(roadmap);
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    public async Task<int> GetProgressPercentAsync(string userId, string roadmapId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        return roadmap?.ProgressPercent ?? 0;
    }

    private static void RecalculateMastery(GrowthRoadmap roadmap)
    {
        var stages = new[] { "Beginner", "Intermediate", "Advanced", "Application" };
        var totalMasterySum = 0;

        foreach (var st in stages)
        {
            var stageResources = roadmap.Resources.Where(r => r.Level.Equals(st, StringComparison.OrdinalIgnoreCase)).ToList();
            if (stageResources.Count == 0)
            {
                roadmap.StageMastery[st] = 0;
                continue;
            }

            var completedResources = stageResources.Count(r => r.IsCompleted || roadmap.CompletedResourceIds.Contains(r.Id));
            var exerciseCount = stageResources.Count(r => !string.IsNullOrEmpty(r.PracticeExercise));
            var completedExercises = stageResources.Count(r => roadmap.CompletedExerciseIds.Contains(r.Id));

            var quizRecord = roadmap.QuizResults.LastOrDefault(q => q.Stage.Equals(st, StringComparison.OrdinalIgnoreCase));
            var quizFactor = quizRecord != null ? (quizRecord.Passed ? 100 : quizRecord.ScorePercent) : 0;

            double resourceProgress = (double)completedResources / stageResources.Count * 50.0;
            double exerciseProgress = exerciseCount > 0 ? (double)completedExercises / exerciseCount * 25.0 : 25.0;
            double quizProgress = quizFactor * 0.25;

            int stagePercent = Math.Min(100, (int)Math.Round(resourceProgress + exerciseProgress + quizProgress));
            roadmap.StageMastery[st] = stagePercent;
            totalMasterySum += stagePercent;

            var mod = roadmap.Modules.FirstOrDefault(m => m.Level.Equals(st, StringComparison.OrdinalIgnoreCase));
            if (mod != null)
            {
                mod.MasteryPercent = stagePercent;
                mod.IsCompleted = stagePercent >= 80;
            }
        }

        roadmap.ProgressPercent = totalMasterySum / stages.Length;

        // Auto-advance stage if user reached mastery
        if (roadmap.StageMastery["Beginner"] >= 80 && roadmap.CurrentStage == "Beginner")
            roadmap.CurrentStage = "Intermediate";
        else if (roadmap.StageMastery["Intermediate"] >= 80 && roadmap.CurrentStage == "Intermediate")
            roadmap.CurrentStage = "Advanced";
        else if (roadmap.StageMastery["Advanced"] >= 80 && roadmap.CurrentStage == "Advanced")
            roadmap.CurrentStage = "Application";
    }

    /// <summary>
    /// Attaches learning resources. Reads only the YouTube cache here — never the API — so a
    /// roadmap render cannot wait on a network call or spend quota. Anything unresolved falls back
    /// to a search link and is warmed in the background for next time.
    /// </summary>
    private async Task EnrichRoadmapWithResourcesAsync(GrowthRoadmap roadmap, CancellationToken ct = default)
    {
        // Keyed on Interest, with Goal only as a fallback. Modules are built from the goal, so when
        // these disagree the page shows one subject's lessons beside another subject's videos —
        // which is how a user whose goal was "Get Promotion" got a Python machine learning course
        // sitting under their promotion roadmap.
        var skill = !string.IsNullOrWhiteSpace(roadmap.Interest) ? roadmap.Interest : roadmap.Goal;

        // An outcome path is about being promotable in a field, not about studying that field, so
        // the field's curated curriculum is the wrong library to draw from.
        var isOutcomePath = roadmap.Modules.Any(m =>
            m.Title.Contains("Know Where You Actually Stand", StringComparison.OrdinalIgnoreCase)
            || m.Title.Contains("Do Work That Gets Noticed", StringComparison.OrdinalIgnoreCase));

        // Curated pools first where one exists, because a hand-written lesson plan beats a generated
        // one. But they only cover the domains someone wrote by hand, so anything else is built from
        // the learner's own path rather than left empty.
        var allResources = isOutcomePath ? [] : LearningResourceCatalog.GetResourcesForSkill(skill);
        if (allResources.Count == 0)
        {
            var resolved = await ResolveLessonVideosAsync(roadmap, ct);
            allResources = LearningPathResourceBuilder.Build(roadmap, resolved);
        }

        roadmap.Resources = allResources;

        // Sync completion states with tracking lists
        foreach (var r in roadmap.Resources)
        {
            r.IsCompleted = roadmap.CompletedResourceIds.Contains(r.Id);
            r.IsExerciseCompleted = roadmap.CompletedExerciseIds.Contains(r.Id);
        }

        var stages = new[] { "Beginner", "Intermediate", "Advanced", "Application" };

        for (var i = 0; i < roadmap.Modules.Count; i++)
        {
            var module = roadmap.Modules[i];
            if (string.IsNullOrEmpty(module.Level) || (module.Level == "Beginner" && i > 0))
            {
                module.Level = i < stages.Length ? stages[i] : stages[3];
            }

            var stageResources = allResources.Where(r => r.Level.Equals(module.Level, StringComparison.OrdinalIgnoreCase)).ToList();
            module.Resources = stageResources.Count > 0 ? stageResources : allResources;

            foreach (var r in module.Resources)
            {
                r.IsCompleted = roadmap.CompletedResourceIds.Contains(r.Id);
                r.IsExerciseCompleted = roadmap.CompletedExerciseIds.Contains(r.Id);
            }
        }

        if (string.IsNullOrEmpty(roadmap.CurrentStage))
        {
            roadmap.CurrentStage = "Beginner";
        }

        RecalculateMastery(roadmap);
    }


    /// <summary>
    /// Real videos for each lesson, from cache only. Misses are queued for a background search so
    /// the next visit shows an actual video instead of a search link. Quota is spent once per
    /// distinct lesson title across every user on that path, not once per page view.
    /// </summary>
    private async Task<Dictionary<string, YouTubeVideoRef>> ResolveLessonVideosAsync(
        GrowthRoadmap roadmap, CancellationToken ct)
    {
        var resolved = new Dictionary<string, YouTubeVideoRef>();
        if (!_youtube.IsConfigured) return resolved;

        var queries = LearningPathResourceBuilder.QueriesFor(roadmap);
        var misses = new List<string>();

        foreach (var (lessonTitle, query) in queries)
        {
            var cached = await _youtube.GetCachedAsync(query, ct: ct);
            if (cached.Count > 0) resolved[lessonTitle] = cached[0];
            else misses.Add(query);
        }

        if (misses.Count > 0) WarmLessonVideos(misses);
        return resolved;
    }

    /// <summary>
    /// Detached, and capped. Searches cost 100 quota units each against a 10,000/day allowance, so
    /// a single roadmap view must not be able to spend a meaningful share of the day's budget.
    /// </summary>
    private void WarmLessonVideos(List<string> queries)
    {
        var batch = queries.Take(4).ToList();

        _ = Task.Run(async () =>
        {
            foreach (var query in batch)
            {
                try
                {
                    await _youtube.SearchAndCacheAsync(query);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Background YouTube warm failed for {Query}", query);
                }
            }
        });
    }


    /// <summary>
    /// Names the template a goal should use, so a stored roadmap can be compared against what the
    /// current code would build for the same person and rebuilt when it no longer matches.
    /// </summary>
    public static string TemplateKeyFor(string goal, LearningIntent? intent)
    {
        if (intent is { IsOutcomeDriven: true }) return $"career-advancement:{intent.Outcome}";

        var g = (goal ?? string.Empty).ToLowerInvariant();
        if (g.Contains("product")) return "product";
        if (g.Contains("data") || g.Contains("analytics") || g.Contains("sql") || g.Contains("excel") || g.Contains("bi")) return "data";
        if (g.Contains("entrepreneur") || g.Contains("startup") || g.Contains("business")) return "entrepreneurship";
        if (g.Contains("finance") || g.Contains("investment") || g.Contains("banking")) return "finance";
        if (g.Contains("software") || g.Contains("engineer") || g.Contains("developer") || g.Contains("coding")) return "software";
        if (g.Contains("policy") || g.Contains("government") || g.Contains("public")) return "policy";
        if (g.Contains("cyber") || g.Contains("security") || g.Contains("soc") || g.Contains("hacking")
            || g.Contains("threat") || g.Contains("malware") || g.Contains("forensic") || g.Contains("penetration")) return "cybersecurity";

        return "generic";
    }

    // ── Template builder (Clean 4-stage progression: Beginner → Intermediate → Advanced → Application) ──

    /// <param name="goal">
    /// Routing text. Callers deliberately widen this (goal + interest path) to give the keyword
    /// match more to work with.
    /// </param>
    /// <param name="displayGoal">
    /// What the user actually typed, used for anything they will read. Without this split, widening
    /// the routing text leaked into titles as "Foundations of hospital nurse manager Nursing".
    /// </param>
    private static GrowthRoadmap BuildFromTemplate(
        string userId, string goal, string? displayGoal = null, LearningIntent? intent = null)
    {
        var normalized = goal.ToLowerInvariant();

        // An outcome goal ("Get Promotion") names a result, not a subject. Teaching the words in it
        // produced "Foundations of Get Promotion"; what the user needs is how to be promotable in
        // their own field, so it gets its own path rather than a keyword match.
        if (intent is { IsOutcomeDriven: true })
        {
            var outcomeRoadmap = new GrowthRoadmap
            {
                UserId = userId,
                Title = Capitalise(intent.Subject),
                Goal = string.IsNullOrWhiteSpace(displayGoal) ? goal : displayGoal,
                Interest = intent.Domain,
                CurrentStage = "Beginner",
                IsActive = true,
                TemplateKey = TemplateKeyFor(goal, intent),
                Modules = CareerAdvancementModules(intent),
            };
            return outcomeRoadmap;
        }

        var shown = intent?.Subject ?? (string.IsNullOrWhiteSpace(displayGoal) ? goal : displayGoal);

        var modules = normalized switch
        {
            var g when g.Contains("product") => ProductManagementModules(),
            var g when g.Contains("data") || g.Contains("analytics") || g.Contains("sql") || g.Contains("excel") || g.Contains("bi") => DataAnalyticsModules(),
            var g when g.Contains("entrepreneur") || g.Contains("startup") || g.Contains("business") => EntrepreneurshipModules(),
            var g when g.Contains("finance") || g.Contains("investment") || g.Contains("banking") => FinanceModules(),
            var g when g.Contains("software") || g.Contains("engineer") || g.Contains("developer") || g.Contains("coding") => SoftwareEngModules(),
            var g when g.Contains("policy") || g.Contains("government") || g.Contains("public") => PublicPolicyModules(),
            // Checked before the generic branches: "cybersecurity" contains none of the keywords
            // above, so it used to fall through to the default and hand SOC analysts a path that
            // opened with Excel fundamentals and Power BI.
            var g when g.Contains("cyber") || g.Contains("security") || g.Contains("soc")
                    || g.Contains("hacking") || g.Contains("threat") || g.Contains("malware")
                    || g.Contains("forensic") || g.Contains("penetration") => CybersecurityModules(),
            // No keyword matched. Build a skeleton named after the goal the user actually stated
            // rather than quietly serving the data-analytics curriculum to everyone unmatched --
            // a generic-but-honest path beats a confident, wrong one.
            _ => GenericModules(shown),
        };

        var roadmap = new GrowthRoadmap
        {
            UserId   = userId,
            Title    = shown,
            Goal     = shown,
            CurrentStage = "Beginner",
            IsActive = true,
            TemplateKey = TemplateKeyFor(goal, intent),
            Modules  = modules,
        };
        // Resources are attached by the caller, which can await. Keeping this builder pure also
        // keeps it unit-testable without a database or an HTTP client.
        return roadmap;
    }

    private static List<RoadmapModule> DataAnalyticsModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: Foundations & Core Tools",
            StageGoal = "Excel fundamentals, data cleaning, basic statistics, charts and visualisation",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "Excel fundamentals & spreadsheet layout", "45 min"),
                Lesson(2, "Data cleaning & Power Query wrangling", "35 min"),
                Lesson(3, "Basic statistics: central tendency & variance", "30 min"),
                Lesson(4, "Charts and business visual storytelling", "30 min")
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: SQL & Relational Modelling",
            StageGoal = "SQL, advanced Excel, Power BI, data modelling",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "SQL aggregations, multi-table joins & CTEs", "55 min"),
                Lesson(2, "Advanced Excel star schemas & relationships", "45 min"),
                Lesson(3, "Power BI dashboard build from raw CSV", "50 min"),
                Lesson(4, "Relational data modelling & DAX measures", "40 min")
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: Python & Predictive Analytics",
            StageGoal = "Python, advanced statistics, predictive analytics, advanced BI",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Python Pandas & NumPy vectorized wrangling", "55 min"),
                Lesson(2, "Advanced statistics & A/B hypothesis testing", "45 min"),
                Lesson(3, "Predictive analytics: customer churn classification", "50 min"),
                Lesson(4, "Advanced BI: Direct Lake & DAX query optimization", "40 min")
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Real-World Projects & Career",
            StageGoal = "Real-world projects, portfolio, case studies, interview preparation",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "End-to-end analytics capstone project", "60 min"),
                Lesson(2, "Portfolio construction & GitHub/NovyPro showcase", "40 min"),
                Lesson(3, "Business case studies: framing findings into ROI", "45 min"),
                Lesson(4, "Data analyst technical interview preparation & live SQL", "50 min")
            ]
        }
    ];

    private static List<RoadmapModule> ProductManagementModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: PM Foundations & Discovery",
            StageGoal = "Problem definition, customer discovery, writing PRDs",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "What is Product Management & PM First 90 Days", "30 min"),
                Lesson(2, "Customer discovery interviews (The Mom Test)", "30 min"),
                Lesson(3, "Writing crisp Product Requirements Documents (PRDs)", "35 min"),
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: Strategy & Growth Loops",
            StageGoal = "Product strategy, competitive moats, growth loops, RICE prioritization",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Product strategy and 7 Powers moats", "40 min"),
                Lesson(2, "Growth loops vs linear acquisition funnels", "35 min"),
                Lesson(3, "Roadmap prioritization with RICE framework", "30 min"),
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: Retention & Experimentation",
            StageGoal = "Product analytics, retention cohorts, A/B testing at scale",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Diagnosing cohort retention curves and flatlines", "40 min"),
                Lesson(2, "Funnel conversion drop-off analysis", "35 min"),
                Lesson(3, "Designing high-velocity A/B experiments", "35 min"),
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Teardowns & PM Interview",
            StageGoal = "Published product teardown, mock interviews, leadership rounds",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "End-to-end product teardown case study", "60 min"),
                Lesson(2, "Product sense interview questions (CIRCLES framework)", "45 min"),
                Lesson(3, "Analytical metric drop diagnosis and execution rounds", "45 min"),
            ]
        }
    ];

    private static List<RoadmapModule> SoftwareEngModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: Clean Code & Git Foundations",
            StageGoal = "Git workflows, SOLID design principles, unit testing",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "Git branching, rebasing and semantic commits", "35 min"),
                Lesson(2, "SOLID principles in modern application code", "35 min"),
                Lesson(3, "Unit testing and test-driven development basics", "30 min"),
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: REST APIs & Databases",
            StageGoal = "RESTful API design, database indexing, caching strategies",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Designing robust idempotent REST APIs", "40 min"),
                Lesson(2, "PostgreSQL indexing and EXPLAIN ANALYZE tuning", "45 min"),
                Lesson(3, "Authentication, JWT tokens and rate limiting", "35 min"),
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: Distributed Systems & Scale",
            StageGoal = "Event-driven architecture, message queues, Docker & CI/CD",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Message queues with RabbitMQ/Kafka and idempotency", "50 min"),
                Lesson(2, "Containerization with Docker Compose and microservices", "40 min"),
                Lesson(3, "Automated CI/CD pipelines with GitHub Actions", "35 min"),
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Production SaaS & System Design",
            StageGoal = "Full-stack cloud deployment, system design interview prep",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Deploying full-stack SaaS with cloud monitoring", "60 min"),
                Lesson(2, "System design interview architecture patterns", "50 min"),
                Lesson(3, "Technical coding challenges and portfolio presentation", "45 min"),
            ]
        }
    ];

    private static List<RoadmapModule> FinanceModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: 3-Statement Financial Modeling",
            StageGoal = "Income statement, balance sheet, cash flow linking",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "Linking the 3 financial statements dynamically", "45 min"),
                Lesson(2, "Working capital management and EBITDA", "35 min"),
                Lesson(3, "Reading audited corporate annual reports", "30 min"),
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: DCF Valuation & WACC",
            StageGoal = "Discounted cash flow, cost of capital, sensitivity tables",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Forecasting Free Cash Flow to Firm (FCFF)", "45 min"),
                Lesson(2, "Calculating WACC and emerging market risk premiums", "40 min"),
                Lesson(3, "Building 2D valuation sensitivity tables", "35 min"),
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: Fintech Rails & Ledgers",
            StageGoal = "Double-entry ledgers, payment settlement, FX dynamics",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Double-entry ledger design and transaction locks", "45 min"),
                Lesson(2, "Payment rails, clearing and settlement timings", "40 min"),
                Lesson(3, "Multi-currency FX spread modeling", "35 min"),
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Investment Due Diligence",
            StageGoal = "Venture capital investment memo, financial due diligence",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Drafting an institutional Investment Committee memo", "60 min"),
                Lesson(2, "Unit economics: CAC, LTV and payback periods", "40 min"),
                Lesson(3, "Finance interview case studies and modeling tests", "45 min"),
            ]
        }
    ];

    private static List<RoadmapModule> EntrepreneurshipModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: Customer Discovery & Validation",
            StageGoal = "Idea validation, Mom Test interviews, MVP definition",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "Customer discovery interviews (The Mom Test)", "40 min"),
                Lesson(2, "Defining a minimum viable product (MVP)", "35 min"),
                Lesson(3, "Problem validation scorecards", "30 min"),
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: Business Models & Unit Economics",
            StageGoal = "Pricing strategy, revenue models, CAC and LTV",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Selecting viable software revenue models", "35 min"),
                Lesson(2, "Calculating unit economics (CAC, LTV, margins)", "40 min"),
                Lesson(3, "First sales and B2B pilot agreements", "35 min"),
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: Distribution & Scaling",
            StageGoal = "Go-to-market channels, viral loops, hiring",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "GTM distribution channels in emerging markets", "40 min"),
                Lesson(2, "Building viral and content-led growth loops", "35 min"),
                Lesson(3, "Hiring your first 5 core team members", "30 min"),
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Pitch Deck & Fundraising",
            StageGoal = "10-slide YC pitch deck, investor presentations, term sheets",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Building a 10-slide YC-style investor pitch deck", "60 min"),
                Lesson(2, "Term sheets, valuations and cap tables", "45 min"),
                Lesson(3, "Pitching to angels and venture funds", "45 min"),
            ]
        }
    ];

    private static List<RoadmapModule> PublicPolicyModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: Policy Analysis Foundations",
            StageGoal = "Stakeholder mapping, policy analysis frameworks",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "Policy formulation and stakeholder mapping", "35 min"),
                Lesson(2, "Data sovereignty and regional governance", "35 min"),
                Lesson(3, "Regulatory impact assessment basics", "30 min"),
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: Economic & Tech Policy",
            StageGoal = "Cross-border data flows, fintech regulation, trade",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Cross-border data flows and African free trade", "40 min"),
                Lesson(2, "Fintech regulatory sandboxes and central bank policies", "40 min"),
                Lesson(3, "Public-private partnerships in digital infrastructure", "35 min"),
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: AI Governance & Sovereignty",
            StageGoal = "National AI strategies, algorithmic accountability",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "National AI strategies and safety standards", "45 min"),
                Lesson(2, "Algorithmic accountability and bias audits", "40 min"),
                Lesson(3, "International treaty negotiations on technology", "35 min"),
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Policy Briefs & Advocacy",
            StageGoal = "Executive policy memos, legislative testimony",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Drafting a 2-page executive policy brief for ministers", "60 min"),
                Lesson(2, "Media engagement, op-eds and public advocacy", "40 min"),
                Lesson(3, "Presenting at legislative hearings and committee sessions", "45 min"),
            ]
        }
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<RoadmapModule> CybersecurityModules() =>
    [
        new()
        {
            Order = 1,
            Level = "Beginner",
            Title = "Beginner: Networking & Security Foundations",
            StageGoal = "Networking fundamentals, the CIA triad, operating system internals, common attack types",
            IsLocked = false,
            Lessons =
            [
                Lesson(1, "TCP/IP, DNS and HTTP: how traffic actually moves", "50 min"),
                Lesson(2, "The CIA triad and core security principles", "30 min"),
                Lesson(3, "Linux and Windows internals for defenders", "45 min"),
                Lesson(4, "Common attack types: phishing, malware, credential theft", "40 min")
            ]
        },
        new()
        {
            Order = 2,
            Level = "Intermediate",
            Title = "Intermediate: Defensive Operations & Monitoring",
            StageGoal = "Firewalls, IDS/IPS, SIEM, log analysis, the incident response lifecycle",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Firewalls, IDS/IPS and network segmentation", "50 min"),
                Lesson(2, "SIEM fundamentals: ingesting and correlating logs", "55 min"),
                Lesson(3, "Packet and log analysis with Wireshark and Zeek", "50 min"),
                Lesson(4, "The incident response lifecycle: detect, contain, eradicate, recover", "45 min")
            ]
        },
        new()
        {
            Order = 3,
            Level = "Advanced",
            Title = "Advanced: Threat Detection & Adversary Emulation",
            StageGoal = "Threat hunting, detection engineering, MITRE ATT&CK, cloud and identity security",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "MITRE ATT&CK and threat-informed defence", "50 min"),
                Lesson(2, "Detection engineering: writing and tuning Sigma rules", "55 min"),
                Lesson(3, "Threat hunting hypotheses and proactive investigation", "50 min"),
                Lesson(4, "Cloud and identity security: IAM misconfiguration and privilege escalation", "50 min")
            ]
        },
        new()
        {
            Order = 4,
            Level = "Application",
            Title = "Application: Home Lab, Portfolio & Interview Prep",
            StageGoal = "A working detection lab, documented incident write-ups, certification and interview readiness",
            IsLocked = true,
            Lessons =
            [
                Lesson(1, "Build a home SOC lab with Security Onion or Wazuh", "60 min"),
                Lesson(2, "Write an incident report that a hiring manager will read", "45 min"),
                Lesson(3, "Capture-the-flag practice and blue-team exercises", "55 min"),
                Lesson(4, "SOC analyst interview preparation: triage scenarios and live questions", "50 min")
            ]
        }
    ];

    /// <summary>
    /// Fallback path for a goal no template covers. Deliberately names the user's own goal at every
    /// stage instead of pretending to be a curriculum Radar does not have, giving the AI pathway
    /// generator and Learn Hub an honest scaffold to fill with real resources.
    /// </summary>
    private static List<RoadmapModule> GenericModules(string goal)
    {
        var subject = SubjectPhrase.Clean(goal);

        return
        [
            new()
            {
                Order = 1,
                Level = "Beginner",
                Title = $"Beginner: Foundations of {subject}",
                StageGoal = $"Core concepts, vocabulary and tools used in {subject}",
                IsLocked = false,
                Lessons =
                [
                    Hinted(1, $"What {subject} actually involves day to day", "40 min", $"what does a {subject} do"),
                    Hinted(2, $"Core concepts and vocabulary in {subject}", "40 min", $"{subject} for beginners"),
                    Hinted(3, $"The standard toolset for {subject}", "45 min", $"{subject} tools explained")
                ]
            },
            new()
            {
                Order = 2,
                Level = "Intermediate",
                Title = $"Intermediate: Applied {subject}",
                StageGoal = $"Working through realistic {subject} problems end to end",
                IsLocked = true,
                Lessons =
                [
                    Hinted(1, $"Typical workflows in {subject}", "50 min", $"{subject} step by step guide"),
                    Hinted(2, $"Working a realistic {subject} problem end to end", "55 min", $"{subject} case study walkthrough"),
                    Hinted(3, "Common mistakes and how practitioners avoid them", "40 min", $"common {subject} mistakes")
                ]
            },
            new()
            {
                Order = 3,
                Level = "Advanced",
                Title = $"Advanced: Depth in {subject}",
                StageGoal = $"Specialisation, scale and judgement in {subject}",
                IsLocked = true,
                Lessons =
                [
                    Hinted(1, $"Specialised techniques in {subject}", "50 min", $"advanced {subject}"),
                    Hinted(2, $"Operating at scale in {subject}", "50 min", $"{subject} at scale"),
                    Hinted(3, "Judgement calls experienced practitioners make", "45 min", $"{subject} best practices")
                ]
            },
            new()
            {
                Order = 4,
                Level = "Application",
                Title = $"Application: Portfolio & Career in {subject}",
                StageGoal = $"Evidence of capability in {subject}, and the interview to prove it",
                IsLocked = true,
                Lessons =
                [
                    Hinted(1, $"Build a portfolio piece that demonstrates {subject}", "60 min", $"{subject} portfolio project"),
                    Hinted(2, "Write up your work so a hiring manager can assess it", "45 min", $"how to present {subject} work"),
                    Hinted(3, $"Interview preparation for {subject} roles", "50 min", $"{subject} interview questions")
                ]
            }
        ];
    }

    /// <summary>
    /// Path for someone chasing an outcome — a promotion, a first job, a step into leadership —
    /// rather than a subject. The lessons are about being promotable in their own field, which is
    /// the real work; "Foundations of Get Promotion" was the old template trying to teach the word.
    /// </summary>
    private static List<RoadmapModule> CareerAdvancementModules(LearningIntent intent)
    {
        var field = SubjectPhrase.Lower(intent.Domain);
        var outcome = intent.Outcome;

        var finalStage = outcome switch
        {
            LearningOutcome.NewJob => "Application: Interviews & Offers",
            LearningOutcome.Leadership => "Application: Leading Your First Team",
            LearningOutcome.EarnMore => "Application: Negotiating Your Value",
            _ => "Application: Making the Case",
        };

        return
        [
            new()
            {
                Order = 1,
                Level = "Beginner",
                Title = "Beginner: Know Where You Actually Stand",
                StageGoal = $"An honest read of your current level in {field}, and what the next level requires",
                IsLocked = false,
                Lessons =
                [
                    Hinted(1, $"What the next level up actually requires in {field}", "40 min",
                        $"{field} career levels explained"),
                    Hinted(2, "Read your own role against that bar, honestly", "35 min",
                        $"{field} skills gap assessment"),
                    Hinted(3, "Find the two gaps that matter most", "30 min",
                        $"how to identify skill gaps career"),
                ]
            },
            new()
            {
                Order = 2,
                Level = "Intermediate",
                Title = "Intermediate: Do Work That Gets Noticed",
                StageGoal = $"Deliver visible, measurable results in {field} and keep a record of them",
                IsLocked = true,
                Lessons =
                [
                    Hinted(1, "Pick work that is visible and actually matters", "45 min",
                        $"high impact projects {field}"),
                    Hinted(2, "Measure your results so you can prove them later", "40 min",
                        "how to quantify your work impact"),
                    Hinted(3, "Keep a running record of what you delivered", "30 min",
                        "brag document career achievements"),
                ]
            },
            new()
            {
                Order = 3,
                Level = "Advanced",
                Title = "Advanced: Influence Beyond Your Own Desk",
                StageGoal = "Communicate, mentor and build the relationships that decisions depend on",
                IsLocked = true,
                Lessons =
                [
                    Hinted(1, "Explain technical work to people who decide budgets", "45 min",
                        "explaining technical work to executives"),
                    Hinted(2, "Mentor someone, and let your judgement show", "40 min",
                        "how to mentor junior colleagues"),
                    Hinted(3, "Find a sponsor, not just a mentor", "35 min",
                        "career sponsor vs mentor"),
                ]
            },
            new()
            {
                Order = 4,
                Level = "Application",
                Title = finalStage,
                StageGoal = "Put the evidence together and have the conversation",
                IsLocked = true,
                Lessons =
                [
                    Hinted(1, "Write the one page case for your step up", "50 min",
                        "how to ask for a promotion"),
                    Hinted(2, "Handle the conversation and the pushback", "45 min",
                        "salary negotiation conversation"),
                    Hinted(3, "Agree what happens next, in writing", "30 min",
                        "career development plan with manager"),
                ]
            }
        ];
    }

    /// <summary>A lesson that also carries a concise phrase to search for it.</summary>
    private static RoadmapLesson Hinted(int order, string title, string time, string searchHint) =>
        new() { Title = title, Order = order, EstimatedTime = time, SearchHint = searchHint };


    private static string Capitalise(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static RoadmapLesson Lesson(int order, string title, string time) =>
        new() { Title = title, Order = order, EstimatedTime = time };
}

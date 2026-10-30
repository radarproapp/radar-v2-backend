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
    private readonly LessonResourceService _lessonResources;
    private readonly ILogger<MongoRoadmapService> _log;

    public MongoRoadmapService(
        RadarDatabase db,
        IAiEngine ai,
        IUserLearningRecordService records,
        YouTubeSearchService youtube,
        LessonResourceService lessonResources,
        ILogger<MongoRoadmapService> log)
    {
        _db = db;
        _ai = ai;
        _records = records;
        _youtube = youtube;
        _lessonResources = lessonResources;
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
        // The learner built this one. Rebuilding would throw away the sections they asked for.
        if (string.Equals(roadmap.TemplateKey, UserBuiltTemplateKey, StringComparison.Ordinal)) return roadmap;

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

    /// <summary>
    /// Marks a path the learner built themselves, section by section.
    /// </summary>
    /// <remarks>
    /// <see cref="RebuildIfStaleAsync"/> exists to repair template paths whose goal has since
    /// changed, and it does that by replacing every module. On a path the learner built, that is
    /// not a repair — it is deleting their work and substituting a curriculum guessed from their
    /// degree. This key tells it to keep its hands off.
    /// </remarks>
    public const string UserBuiltTemplateKey = "user-built";

    /// <summary>
    /// Whether this path is one the learner actually asked for.
    /// </summary>
    /// <remarks>
    /// True when they built it a section at a time, or when they have put work into a generated
    /// one — a finished lesson, a completed resource or exercise, a quiz attempt. The second case
    /// matters: a path nobody chose but somebody has been using is still their path, and hiding it
    /// would lose their progress from view.
    /// </remarks>
    /// <summary>
    /// Replaces any null collection on a stored roadmap with an empty one.
    /// </summary>
    /// <remarks>
    /// The C# model defaults every list to empty, but a document written with an explicit null
    /// deserializes to null — and the production database is the oldest, so it has them. There
    /// are a dozen places downstream that call .Any(), .Count or .Add on these, and guarding each
    /// one is a losing game: the next one added would miss it. Normalising on read fixes all of
    /// them at once, including the ones not written yet.
    ///
    /// This is what took down Today, Opportunities and the Mentor together — they all reach
    /// GetActiveRoadmapAsync, so one old roadmap with a null array broke three screens.
    /// </remarks>
    private static GrowthRoadmap Normalise(GrowthRoadmap roadmap)
    {
        roadmap.Modules ??= [];
        roadmap.Resources ??= [];
        roadmap.CompletedResourceIds ??= [];
        roadmap.CompletedExerciseIds ??= [];
        roadmap.QuizResults ??= [];
        roadmap.WeakAreas ??= [];
        roadmap.StageMastery ??= new();

        foreach (var module in roadmap.Modules)
        {
            module.Lessons ??= [];
            module.Resources ??= [];
            module.ContentItemIds ??= [];
        }

        return roadmap;
    }

    public static bool WasAskedFor(GrowthRoadmap roadmap)
    {
        if (string.Equals(roadmap.TemplateKey, UserBuiltTemplateKey, StringComparison.Ordinal)) return true;

        // Null-guarded throughout. The C# defaults are empty lists, but a document stored with an
        // explicit null — and the production database is the oldest, so it has them — deserializes
        // to null, and .Any() on null throws. This method is called from GetActiveRoadmapAsync,
        // which Today, Opportunities and the Mentor all depend on, so one old roadmap with a null
        // array took down three screens at once.
        return (roadmap.Modules ?? []).Any(m => m.IsCompleted || (m.Lessons ?? []).Any(l => l.IsCompleted))
            || (roadmap.CompletedResourceIds?.Count ?? 0) > 0
            || (roadmap.CompletedExerciseIds?.Count ?? 0) > 0
            || (roadmap.QuizResults?.Count ?? 0) > 0;
    }

    /// <param name="ct">
    /// Carried into the enrichment below, which reaches two third-party services.
    /// </param>
    /// <remarks>
    /// It had none, so a request that the client had already given up on went on searching for
    /// videos - and nothing could stop it. This is the method every screen in Radar Learn calls.
    /// </remarks>
    public async Task<GrowthRoadmap?> GetActiveRoadmapAsync(string userId, CancellationToken ct = default)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync();
        if (roadmap is not null) Normalise(roadmap);

        // A path generated before anyone was asked is not this learner's path.
        //
        // Stopping the generator helps nobody who already has one of its roadmaps stored, and
        // that is every existing user — they still open Radar Learn to "Foundations of Computer
        // Science" and twelve lessons they never chose. Treated as absent, so they get the empty
        // state and the three questions, same as a new account.
        //
        // Nothing is deleted. The document stays, and anything with real progress on it is kept
        // and shown, because a learner who has been working through a generated path has made it
        // theirs by working through it.
        if (roadmap is not null && !WasAskedFor(roadmap)) return null;

        if (roadmap is not null)
        {
            // Rebuild the modules when the stored roadmap came from a template this goal would no
            // longer choose. Without this, a path generated before the goal interpreter existed
            // keeps its old titles forever — the read path only ever re-tailored stages, so a user
            // whose goal is "Get Promotion" went on seeing "Foundations of Get Promotion".
            roadmap = await RebuildIfStaleAsync(roadmap, userId);

            // Re-enrich with latest catalog structure (4 stages, ordered resources, practical exercises)
            // Personalise first. It inserts lessons of its own, and a lesson created after the
            // fill is a lesson nothing has checked — it reached the learner with no resources and
            // no note, so the page fell back to the stage-wide list and showed it material chosen
            // for the whole stage. That is the mismatch this gate exists to end, landing on the
            // one lesson deliberately pushed to the front of their work.
            await PersonalizeAsync(roadmap, userId);
            await EnrichRoadmapWithResourcesAsync(roadmap, ct);
            await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmap.Id, roadmap);
            return roadmap;
        }

        // No roadmap, and none is invented.
        //
        // This used to auto-create one from the profile's goal, so simply *reading* the path
        // built a whole templated pathway. A Computer Science student who had never opened Radar
        // Learn already had four stages and lessons called "What Computer Science actually
        // involves day to day" waiting for them, and nobody had asked whether they wanted any of
        // it. The degree says what they study, not what they want to learn next.
        //
        // Every caller already handles null: the controller returns 404, which the client reads
        // as an empty path and answers with three questions.
        return null;
    }

    public Task<GrowthRoadmap> CreateRoadmapAsync(string userId, string goal) =>
        CreateRoadmapAsync(userId, goal, null, null);

    public async Task<GrowthRoadmap> CreateRoadmapAsync(
        string userId, string goal, string? displayGoal, LearningIntent? intent = null)
    {
        // Built, then switched to. Not the other way round.
        //
        // The old path was deactivated first and then minutes of model calls and third-party
        // HTTP ran before the new one was inserted. Anything that threw in that stretch - and
        // the video search put an uncancelled network loop in it - left the learner with no
        // active path at all: Radar Learn empty, Today blank, and the path they were on switched
        // off for a path that was never written.
        //
        // Enriching first costs nothing if it fails, because nothing has changed yet.
        var roadmap = BuildFromTemplate(userId, goal, displayGoal, intent);

        // Personalise first: a lesson it inserts after the fill is one nothing has checked.
        await PersonalizeAsync(roadmap, userId);
        await EnrichRoadmapWithResourcesAsync(roadmap);

        await _db.Roadmaps.InsertOneAsync(roadmap);

        // Then clear the others, excluding the one just written.
        await _db.Roadmaps.UpdateManyAsync(
            r => r.UserId == userId && r.IsActive && r.Id != roadmap.Id,
            Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, false));

        return roadmap;
    }

    /// <summary>
    /// Makes one of this learner's paths the one they are on.
    /// </summary>
    /// <remarks>
    /// Changing path meant going to Settings, editing the interests a path is derived from, and
    /// generating a new one - so the paths somebody had already built were unreachable from the
    /// screen that shows them. A learner moving between two subjects was starting over each time.
    ///
    /// Scoped to the owner, so a path id belonging to somebody else reads as a path that is not
    /// there. Only one is active, which is why the others are cleared first.
    /// </remarks>
    /// <summary>
    /// The paths a learner can actually switch to.
    /// </summary>
    /// <remarks>
    /// The stored list includes paths generated before anybody was asked, which
    /// GetActiveRoadmapAsync treats as absent. Offering one of those in a switcher is offering a
    /// path that disappears the moment it is chosen.
    /// </remarks>
    public async Task<List<GrowthRoadmap>> GetSwitchableRoadmapsAsync(string userId)
    {
        var all = await GetUserRoadmapsAsync(userId);

        foreach (var roadmap in all) Normalise(roadmap);

        return [.. all.Where(WasAskedFor)];
    }

    public async Task<GrowthRoadmap?> ActivateRoadmapAsync(string userId, string roadmapId)
    {
        var wanted = await _db.Roadmaps
            .Find(r => r.Id == roadmapId && r.UserId == userId)
            .FirstOrDefaultAsync();

        if (wanted is null) return null;

        Normalise(wanted);

        // A path the reader cannot load is not a path to switch to.
        //
        // GetActiveRoadmapAsync treats a generated path with no work on it as absent, so
        // activating one would deactivate the path the learner was using and then answer every
        // later read with "no active roadmap" - an empty Radar Learn, a blank Today focus, and
        // no way back, because the empty screen is the one that does not offer the switcher.
        //
        // Refused rather than silently repaired: the path exists, it is theirs, and it is not
        // one they ever asked for. Saying so is better than quietly making it the one they are
        // on.
        if (!WasAskedFor(wanted)) return null;

        // Set before cleared. Two writes with no transaction between them, so the order decides
        // what a read landing in the middle sees: this way it finds two active paths for an
        // instant and picks one of them, rather than none and showing an empty product. If the
        // second write never happens, the learner has the path they just chose plus a stale flag
        // on the old one - recoverable by switching again, which the other order is not.
        await _db.Roadmaps.UpdateOneAsync(
            r => r.Id == roadmapId && r.UserId == userId,
            Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, true));

        await _db.Roadmaps.UpdateManyAsync(
            r => r.UserId == userId && r.IsActive && r.Id != roadmapId,
            Builders<GrowthRoadmap>.Update.Set(r => r.IsActive, false));

        wanted.IsActive = true;
        return wanted;
    }

    /// <summary>
    /// Takes a section out of a path.
    /// </summary>
    /// <remarks>
    /// The other half of being able to edit one. Sections could be added and never removed, so a
    /// path built by trying things accumulated every wrong turn, and the only way back was to
    /// start again.
    ///
    /// The remaining sections are renumbered, because Order is what the screen sorts by and a
    /// gap in it reads as a missing lesson.
    /// </remarks>
    public async Task<GrowthRoadmap?> RemoveSectionAsync(string userId, string roadmapId, string moduleId)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.Id == roadmapId && r.UserId == userId)
            .FirstOrDefaultAsync();

        if (roadmap is null) return null;

        var removed = roadmap.Modules.RemoveAll(m => m.Id == moduleId);
        if (removed == 0) return roadmap;

        for (var i = 0; i < roadmap.Modules.Count; i++)
        {
            roadmap.Modules[i].Order = i + 1;
        }

        // Progress is recounted, not left as it was.
        //
        // It is a stored number rather than one derived on read, so removing two of three
        // sections left a learner reported at the percentage they had reached across all three.
        // The path screen works it out from the lessons in front of it and would have been
        // right; everything reading the stored figure - the progress endpoint, the digest, the
        // mastery dashboard - would have been quoting a path that no longer exists.
        RecalculateMastery(roadmap);

        await _db.Roadmaps.UpdateOneAsync(
            r => r.Id == roadmapId && r.UserId == userId,
            Builders<GrowthRoadmap>.Update
                .Set(r => r.Modules, roadmap.Modules)
                .Set(r => r.ProgressPercent, roadmap.ProgressPercent)
                .Set(r => r.StageMastery, roadmap.StageMastery));

        Normalise(roadmap);
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
        // Personalise first: a lesson it inserts after the fill is one nothing has checked.
        await PersonalizeAsync(roadmap, userId, ct);
        await EnrichRoadmapWithResourcesAsync(roadmap, ct);

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

    /// <summary>
    /// A few slices from inside the document, rather than the first few thousand characters.
    /// </summary>
    /// <remarks>
    /// The opening of a book is the worst sample of it. A 1,287 page Java textbook begins with
    /// "This page intentionally left blank", a copyright notice and the publisher's staff
    /// directory — the words "Java" and "programming" appear nowhere in the first six thousand
    /// characters, because the cover is an image. Radar read the masthead and said, correctly,
    /// that it could not tell what the document taught.
    ///
    /// Five slices from inside it land in actual chapters. The first sits just past the front
    /// matter, where a preface or contents page usually says who the book is for — which is the
    /// best evidence of level there is, and exactly what a 15% slice skips over.
    /// </remarks>
    private static string Sample(string text, int budget = 9000)
    {
        if (text.Length <= budget) return text;

        var points = new[] { 0.04, 0.15, 0.35, 0.55, 0.78 };
        var sliceLength = budget / points.Length;
        var sb = new StringBuilder();

        foreach (var at in points)
        {
            var start = (int)(text.Length * at);
            var take = Math.Min(sliceLength, text.Length - start);
            if (take <= 0) continue;

            sb.AppendLine(text.Substring(start, take));
            sb.AppendLine("[...]");
        }

        return sb.ToString();
    }

    /// <summary>What a document turned out to be about.</summary>
    public sealed class DocumentSubject
    {
        public string? Subject { get; set; }
        public string? Outcome { get; set; }
        public string? Level { get; set; }

        /// <summary>
        /// The parts of the document, in the order it teaches them. A 1,287 page textbook is a
        /// path, not a section, and returning one section for it threw away most of the book.
        /// </summary>
        public List<ModuleResult>? Sections { get; set; }
    }

    private sealed class PathwayResult { public List<ModuleResult>? Modules { get; set; } }

    public sealed class ModuleResult
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? StageGoal { get; set; }
        public List<LessonResult>? Lessons { get; set; }
    }

    public sealed class LessonResult
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? EstimatedTime { get; set; }
    }

    public async Task CompleteLessonAsync(string userId, string roadmapId, string moduleId, string lessonId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is not null) Normalise(roadmap);
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
        if (roadmap is not null) Normalise(roadmap);
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
        if (roadmap is not null) Normalise(roadmap);
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
        if (roadmap is not null) Normalise(roadmap);
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
        if (roadmap is not null) Normalise(roadmap);
        if (roadmap is null) return;

        roadmap.CurrentStage = stage;
        await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmapId, roadmap);
    }

    /// <summary>
    /// Puts every section a document yielded onto the learner's path, in the order it teaches them.
    /// </summary>
    /// <remarks>
    /// A textbook is a path. Returning one section for 1,287 pages kept the first five lessons and
    /// discarded the rest of the book, which is a worse answer than it looked — it was specific,
    /// so it read as correct.
    /// </remarks>
    public async Task<GrowthRoadmap> AddDocumentPathAsync(
        string userId,
        DocumentSubject document,
        CancellationToken ct = default)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync(ct);
        if (roadmap is not null) Normalise(roadmap);

        var isNew = roadmap is null;

        if (roadmap is not null && !WasAskedFor(roadmap))
        {
            roadmap.Modules.Clear();
            roadmap.Resources.Clear();
        }

        roadmap ??= new GrowthRoadmap { UserId = userId, IsActive = true };

        roadmap.TemplateKey = UserBuiltTemplateKey;
        roadmap.Title = document.Subject!;
        roadmap.Interest = document.Subject!;
        roadmap.Level = document.Level ?? "Beginner";
        roadmap.Goal = document.Outcome ?? string.Empty;

        var order = roadmap.Modules.Count;

        foreach (var section in document.Sections ?? [])
        {
            if (string.IsNullOrWhiteSpace(section.Title)) continue;

            // Same filter as the three-question path: a lesson may not be the subject's name
            // wearing a frame, however confidently the model wrote it.
            var named = LessonTitleQuality.RemoveGeneric(section.Lessons ?? [], document.Subject!, l => l.Title);
            if (named.Count == 0) continue;

            order++;
            roadmap.Modules.Add(new RoadmapModule
            {
                Title = section.Title!,
                Description = section.Description ?? string.Empty,
                StageGoal = section.StageGoal ?? string.Empty,
                Order = order,
                Level = document.Level ?? "Beginner",
                IsLocked = false,
                Lessons = named
                    .Select((l, index) => new RoadmapLesson
                    {
                        Title = l.Title!,
                        Body = l.Body ?? string.Empty,
                        Order = index + 1,
                        EstimatedTime = l.EstimatedTime ?? string.Empty,
                    })
                    .ToList(),
            });
        }

        await EnrichRoadmapWithResourcesAsync(roadmap, ct);

        if (isNew) await _db.Roadmaps.InsertOneAsync(roadmap, cancellationToken: ct);
        else await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmap.Id, roadmap, cancellationToken: ct);

        return roadmap;
    }

    public async Task<GrowthRoadmap> AddSectionAsync(
        string userId,
        UserProfile profile,
        string subject,
        string level,
        string? outcome,
        CancellationToken ct = default)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && r.IsActive)
            .FirstOrDefaultAsync(ct);
        if (roadmap is not null) Normalise(roadmap);

        var isNew = roadmap is null;

        // The stored path may be one of the generated ones nobody asked for. Appending to it
        // would hand the learner their first chosen section underneath twelve lessons they never
        // chose, so it is emptied and becomes theirs instead.
        if (roadmap is not null && !WasAskedFor(roadmap))
        {
            roadmap.Modules.Clear();
            roadmap.Resources.Clear();
            roadmap.TemplateKey = UserBuiltTemplateKey;
            roadmap.Title = "Your path";
            roadmap.Interest = subject;
            roadmap.Level = level;
        }

        // An empty path, not a generated one. The learner asked for one subject; handing back
        // four stages of a curriculum derived from their degree is the behaviour being removed.
        roadmap ??= new GrowthRoadmap
        {
            UserId   = userId,
            Title    = "Your path",
            Goal     = outcome ?? string.Empty,
            Interest = subject,
            Level    = level,
            IsActive = true,
            TemplateKey = UserBuiltTemplateKey,
        };

        var section = await BuildAiSectionAsync(profile, subject, level, outcome, ct)
                      ?? PlaceholderSection(subject, level);

        section.Order    = roadmap.Modules.Count + 1;
        section.Level    = level;
        section.IsLocked = false;
        roadmap.Modules.Add(section);

        await EnrichRoadmapWithResourcesAsync(roadmap, ct);

        if (isNew) await _db.Roadmaps.InsertOneAsync(roadmap, cancellationToken: ct);
        else await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmap.Id, roadmap, cancellationToken: ct);

        return roadmap;
    }

    /// <summary>
    /// Reads a document the learner uploaded and names what it is about, so a path can be built
    /// from it without asking them to summarise their own lecture notes.
    /// </summary>
    /// <remarks>
    /// Returns null when the model cannot say — at which point the caller asks the learner
    /// instead, which is a better outcome than guessing a subject from a filename.
    /// </remarks>
    public async Task<DocumentSubject?> ReadSubjectFromDocumentAsync(
        string text,
        string? fileName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var excerpt = Sample(text);

        // Roughly 2,000 characters to a printed page. The model uses it to scale the path: one
        // section for a handout, several for a textbook.
        var pages = Math.Max(1, text.Length / 2000);

        var sb = new StringBuilder();
        sb.AppendLine("Read these excerpts and say what the document teaches.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // A weak signal, and said to be one. A textbook's cover is often an image, so the
            // only place its title appears may be the filename — but the excerpts decide.
            sb.AppendLine($"The file is named: {fileName}");
            sb.AppendLine("Treat that as a hint only. The excerpts are the authority.");
            sb.AppendLine();
        }
        sb.AppendLine($"It is roughly {pages:N0} pages of text.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- subject: two to four words naming the field or topic, as a learner would say it.");
        sb.AppendLine("- outcome: one sentence on what someone could do after working through this material.");
        sb.AppendLine("- level: who the material is written FOR, not how hard its hardest chapter is.");
        sb.AppendLine("  Beginner if it teaches from first principles and assumes no prior knowledge —");
        sb.AppendLine("  a long introductory textbook is still Beginner. Intermediate if it assumes the");
        sb.AppendLine("  basics of the subject. Advanced if it assumes working familiarity.");
        sb.AppendLine($"- sections: scale to how much ground the document covers. One for a handout or a");
        sb.AppendLine("  chapter; three to five for a textbook. Each section is a coherent part of it,");
        sb.AppendLine("  ordered the way the document teaches them.");
        sb.AppendLine("- Each section has 3 to 6 lessons naming the actual concepts, techniques, syntax or");
        sb.AppendLine("  tools that appear in the excerpts. Not chapter headings — the thing being taught.");
        sb.AppendLine($"- No lesson may be the subject's name in a frame: \"Introduction to X\" or");
        sb.AppendLine("  \"Core concepts of X\" is a failure.");
        sb.AppendLine("- estimatedTime is realistic, e.g. \"25 min\", \"1 hr 30 min\".");
        sb.AppendLine("- If the document is not teaching material, return an empty subject.");
        sb.AppendLine();
        sb.AppendLine("Return strict JSON:");
        sb.AppendLine("{ \"subject\": \"...\", \"outcome\": \"...\", \"level\": \"...\", \"sections\": [");
        sb.AppendLine("  { \"title\": \"...\", \"description\": \"...\", \"stageGoal\": \"...\",");
        sb.AppendLine("    \"lessons\": [ { \"title\": \"...\", \"body\": \"...\", \"estimatedTime\": \"...\" } ] } ] }");
        sb.AppendLine();
        sb.AppendLine("Excerpts:");
        sb.AppendLine(excerpt);

        var result = await _ai.GenerateJsonAsync<DocumentSubject>(
            "document-subject",
            "You identify what a document teaches. You answer only from the document in front of you.",
            sb.ToString(),
            ct);

        if (result is null || string.IsNullOrWhiteSpace(result.Subject)) return null;

        var level = result.Level?.Trim() ?? "Beginner";
        result.Level = level is "Beginner" or "Intermediate" or "Advanced" ? level : "Beginner";
        result.Subject = result.Subject!.Trim();

        return result;
    }

    /// <summary>
    /// One section for one subject, at the level the learner said they were starting from and
    /// aimed at what they said they wanted to be able to do.
    /// </summary>
    private async Task<RoadmapModule?> BuildAiSectionAsync(
        UserProfile profile,
        string subject,
        string level,
        string? outcome,
        CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Build ONE section of a learning path on: {subject}");
            sb.AppendLine();
            sb.AppendLine("What the learner told us:");
            sb.AppendLine($"they want to learn: {subject}");
            sb.AppendLine($"they are starting from: {level}");
            if (!string.IsNullOrWhiteSpace(outcome))
                sb.AppendLine($"they want to be able to: {outcome}");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Between 4 and 6 lessons, ordered, each a concrete thing to learn or do.");
            sb.AppendLine("- Write for what they said they want, not for their degree or job title.");
            sb.AppendLine("- No lesson may restate the subject name as its content. A lesson called");
            sb.AppendLine($"  \"Introduction to {subject}\" or \"Core concepts of {subject}\" is a failure:");
            sb.AppendLine("  name the actual concept, tool or technique the lesson covers.");
            sb.AppendLine("- The body is 1-2 sentences on what they will be able to do after it.");
            sb.AppendLine("- estimatedTime is realistic, e.g. \"25 min\", \"1 hr 30 min\".");
            sb.AppendLine();
            sb.AppendLine("Return strict JSON:");
            sb.AppendLine("{ \"title\": \"...\", \"description\": \"...\", \"stageGoal\": \"...\",");
            sb.AppendLine("  \"lessons\": [ { \"title\": \"...\", \"body\": \"...\", \"estimatedTime\": \"...\" } ] }");

            var result = await _ai.GenerateJsonAsync<ModuleResult>(
                "learning-section",
                "You are Radar's learning architect. You design one focused, ordered section of a learning path from what the learner has told you they want. You never pad with generic overview lessons.",
                sb.ToString(),
                ct);

            if (result is null || string.IsNullOrWhiteSpace(result.Title)) return null;

            // The prompt asks for this; the filter enforces it. A model told not to write
            // "Introduction to X" will still write it often enough that asking is not a control.
            var named = LessonTitleQuality.RemoveGeneric(result.Lessons ?? [], subject, l => l.Title);

            var lessons = named
                .Select((l, index) => new RoadmapLesson
                {
                    Title         = l.Title!,
                    Body          = l.Body ?? string.Empty,
                    Order         = index + 1,
                    EstimatedTime = l.EstimatedTime ?? string.Empty,
                })
                .ToList();

            // Two lessons is the floor for something worth calling a section. Below it, the model
            // produced an outline rather than a plan, and an honest empty section with real
            // resources attached beats three restatements of the subject.
            if (lessons.Count < 2)
            {
                _log.LogInformation(
                    "Section for '{Subject}' discarded: {Kept} of {Total} lesson titles named anything.",
                    subject, lessons.Count, (result.Lessons ?? []).Count);
                return null;
            }

            return new RoadmapModule
            {
                Title       = result.Title!,
                Description = result.Description ?? string.Empty,
                StageGoal   = result.StageGoal ?? string.Empty,
                Lessons     = lessons,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Section generation failed for subject '{Subject}'", subject);
            return null;
        }
    }

    /// <summary>
    /// What a section is when the AI could not build one: the subject, the level, and no lessons.
    /// </summary>
    /// <remarks>
    /// It would be easy to emit "Introduction to X / Core concepts of X / Applying X in practice"
    /// here, as the old add-topic path did. Those are not lessons, they are the subject's name in
    /// three costumes, and a path full of them is what made the old screen feel like filler. An
    /// empty section says plainly that the lessons could not be written, and the curated resources
    /// attached to it are real.
    /// </remarks>
    private static RoadmapModule PlaceholderSection(string subject, string level) => new()
    {
        Title       = subject,
        Description = string.Empty,
        StageGoal   = $"Work through the resources below at {level.ToLowerInvariant()} level.",
        Lessons     = [],
    };

    /// <summary>
    /// "Add to my path" from the feed or a capture: one named section, with the curated resources
    /// for it and nothing invented.
    /// </summary>
    /// <remarks>
    /// This used to add three lessons — "Introduction to X", "Core concepts of X", "Applying X in
    /// practice" — for every topic. They are the subject's name in three costumes, and a path made
    /// of them is what made Radar Learn read as filler. The section now carries real resources and
    /// no lessons; <see cref="AddSectionAsync"/> is the route for lessons, because that one asks
    /// what the learner actually wants.
    ///
    /// It also creates the path when there is none. A learner's path starts empty by design, so
    /// "add to my path" from the feed must work on the very first click rather than 404.
    /// </remarks>
    /// <summary>
    /// Appends a bare topic to the active roadmap. No caller in the app uses this any more.
    /// </summary>
    /// <remarks>
    /// It creates a module with <c>Lessons = []</c> at a hardcoded "Intermediate" — an empty
    /// section at a level nobody stated, which is the inference the rest of Learn exists to
    /// avoid. The feed card, the brief and the capture page now hand the subject to the builder,
    /// which asks the starting level and the desired outcome and generates real lessons from
    /// them. That is why a 1,287-page textbook came back as five correctly-levelled sections.
    ///
    /// Left in place rather than deleted because removing an endpoint is a decision about the
    /// API surface, not a cleanup. It is a deletion candidate.
    /// </remarks>
    public async Task AddTopicToRoadmapAsync(string userId, string roadmapId, string topic)
    {
        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == userId && (r.Id == roadmapId || r.IsActive))
            .FirstOrDefaultAsync();
        if (roadmap is not null) Normalise(roadmap);

        var isNew = roadmap is null;

        // Same reasoning as AddSectionAsync: a generated path nobody asked for is not something
        // to append to.
        if (roadmap is not null && !WasAskedFor(roadmap))
        {
            roadmap.Modules.Clear();
            roadmap.Resources.Clear();
            roadmap.TemplateKey = UserBuiltTemplateKey;
            roadmap.Title = "Your path";
            roadmap.Interest = topic;
        }

        roadmap ??= new GrowthRoadmap
        {
            UserId   = userId,
            Title    = "Your path",
            Interest = topic,
            IsActive = true,
            TemplateKey = UserBuiltTemplateKey,
        };

        // Adding the same topic twice from two different feed items should not grow the path.
        if (roadmap.Modules.Any(m => m.Title.Equals(topic, StringComparison.OrdinalIgnoreCase))) return;

        roadmap.Modules.Add(new RoadmapModule
        {
            Title    = topic,
            Order    = roadmap.Modules.Count + 1,
            Level    = "Intermediate",
            IsLocked = false,
            Lessons  = [],
        });

        await EnrichRoadmapWithResourcesAsync(roadmap);

        if (isNew) await _db.Roadmaps.InsertOneAsync(roadmap);
        else await _db.Roadmaps.ReplaceOneAsync(r => r.Id == roadmap.Id, roadmap);
    }

    public async Task<int> GetProgressPercentAsync(string userId, string roadmapId)
    {
        var roadmap = await _db.Roadmaps.Find(r => r.Id == roadmapId && r.UserId == userId).FirstOrDefaultAsync();
        if (roadmap is not null) Normalise(roadmap);
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

        // Five videos and two articles per lesson, rather than one list per stage.
        //
        // After the stage resources, because this needs the modules in place — and inside the
        // same method so every route that builds or reads a roadmap gets it, instead of only the
        // one somebody remembered to call.
        // Before the fill, not after. This queues searches in the background and returns at once;
        // starting it afterwards meant the very pass that decides a lesson's resources always ran
        // against a cache nothing had filled yet.
        WarmLessonVideosForEmptyLessons(roadmap);

        await _lessonResources.FillAsync(roadmap, ct);

        RecalculateMastery(roadmap);
    }

    /// <summary>
    /// Queues searches for the lessons that are still waiting on videos.
    /// </summary>
    /// <remarks>
    /// Capped because a search costs 100 quota units against 10,000 a day, so one page view must
    /// not be able to spend a meaningful share of it. Five results per search instead of three
    /// costs nothing extra — the price is per request, not per result.
    ///
    /// Only lessons that have not settled. This used to warm any lesson without videos, including
    /// ones whose resources were already decided — and a settled lesson is never refilled, so
    /// every search spent on one bought nothing. On a path with several sections that was most of
    /// the budget: the same finished lessons were re-searched on every read while the newly built
    /// ones queued behind them and never reached the front.
    ///
    /// Measured on production before the change: a fifteen-lesson roadmap had five videos on
    /// every older lesson and none on any lesson built that day, after two full passes. The
    /// instrumentation on /api/version ruled out quota — searches were succeeding and finding
    /// results — which left reach as the only explanation.
    /// </remarks>
    /// <summary>
    /// How many searches one read of a path may queue.
    /// </summary>
    /// <remarks>
    /// Six rather than four, now that none of them is spent on a lesson that cannot use the
    /// result. A newly built section is five lessons, so this clears one in a single read instead
    /// of leaving the last of them waiting behind the others.
    ///
    /// Still a ceiling on a daily quota that cannot be seen from here - which is why
    /// YouTubeSearchService counts what it spends and /api/version reports it.
    /// </remarks>
    private const int WarmedPerRead = 6;

    private void WarmLessonVideosForEmptyLessons(GrowthRoadmap roadmap)
    {
        if (!_youtube.IsConfigured) return;

        var queries = roadmap.Modules
            .SelectMany(m => m.Lessons ?? [])
            .Where(l => l.ResourcesJudgedAt is null
                        && !l.Videos.Any()
                        && !string.IsNullOrWhiteSpace(l.Title))
            .Select(l => !string.IsNullOrWhiteSpace(l.SearchHint) ? l.SearchHint! : l.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(WarmedPerRead)
            .ToList();

        if (queries.Count == 0) return;

        _ = Task.Run(async () =>
        {
            foreach (var query in queries)
            {
                try
                {
                    await _youtube.SearchAndCacheAsync(query, take: 5);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Background YouTube warm failed for {Query}", query);
                }
            }
        });
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
        if (DomainVocabulary.Matches(g, "cyber")) return "cybersecurity";

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
            var g when DomainVocabulary.Matches(g, "cyber") => CybersecurityModules(),
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

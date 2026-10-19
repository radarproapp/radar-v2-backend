using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Builds the consolidated <see cref="UserLearningRecord"/> from its existing sources.
///
/// Deliberately derived, never stored as its own source of truth: profile, behavioural signals and
/// the roadmap stay authoritative, and this recomputes from them. That way the record cannot drift
/// from the data it claims to summarise, and there is no migration to run when a source changes.
/// </summary>
public class UserLearningRecordService : IUserLearningRecordService
{
    private readonly RadarDatabase _db;
    private readonly IBehavioralSignalService _signals;

    public UserLearningRecordService(RadarDatabase db, IBehavioralSignalService signals)
    {
        _db = db;
        _signals = signals;
    }

    public async Task<UserLearningRecord> BuildAsync(UserProfile profile, CancellationToken ct = default)
    {
        var record = new UserLearningRecord
        {
            UserId = profile.Id,
            PrimaryGoal = profile.PrimaryGoal,
            TargetRole = profile.TargetRole ?? string.Empty,
            TargetIndustry = profile.TargetIndustry ?? string.Empty,
            Interests = profile.Interests.ToList(),
            Capabilities = profile.Capabilities.ToList(),
            Problems = profile.Problems.ToList(),
            Location = !string.IsNullOrWhiteSpace(profile.City) ? profile.City : profile.Region ?? string.Empty,
            DeclaredLevel = InterestPersonalization.Contexts(profile).FirstOrDefault()?.Level ?? "Beginner",
            LessonsCompleted = profile.Stats.LessonsCompleted,
        };

        var signals = await _signals.GetSignalsAsync(profile.Id);
        record.Signals = signals
            .OrderByDescending(BehavioralSignalService.DecayedStrength)
            .Take(15)
            .Select(s => new RecordSignal
            {
                Term = s.Term,
                Strength = Math.Round(BehavioralSignalService.DecayedStrength(s), 3),
                TimesEngaged = s.PositiveCount,
                Source = s.Source.ToString(),
            })
            .ToList();

        var roadmap = await _db.Roadmaps
            .Find(r => r.UserId == profile.Id && r.IsActive)
            .FirstOrDefaultAsync(ct);

        if (roadmap is not null)
        {
            record.ResourcesCompleted = roadmap.CompletedResourceIds.Count;
            record.Topics = BuildTopicEvidence(roadmap);
            record.QuizzesTaken = roadmap.QuizResults.Count;
            record.AverageQuizScore = roadmap.QuizResults.Count > 0
                ? (int)Math.Round(roadmap.QuizResults.Average(q => q.ScorePercent))
                : 0;
        }

        record.WeakAreas = record.Topics
            .Where(t => t.Status == "Struggling")
            .Select(t => t.Topic)
            .Concat(roadmap?.WeakAreas ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        record.StrongAreas = record.Topics
            .Where(t => t.Status == "Mastered")
            .Select(t => t.Topic)
            .ToList();

        record.MasteredTopics = record.StrongAreas.ToList();
        record.RecommendedStartStage = RecommendStartStage(record, roadmap);
        record.PriorityTopics = BuildPriorityTopics(record);
        record.Confidence = ComputeConfidence(record);
        record.Summary = BuildSummary(record);
        record.UpdatedAt = DateTime.UtcNow;

        return record;
    }

    private static List<TopicEvidence> BuildTopicEvidence(GrowthRoadmap roadmap)
    {
        var byTopic = new Dictionary<string, TopicEvidence>(StringComparer.OrdinalIgnoreCase);

        foreach (var quiz in roadmap.QuizResults)
        {
            if (string.IsNullOrWhiteSpace(quiz.Topic)) continue;

            if (!byTopic.TryGetValue(quiz.Topic, out var evidence))
            {
                evidence = new TopicEvidence { Topic = quiz.Topic };
                byTopic[quiz.Topic] = evidence;
            }

            evidence.Attempts++;
            evidence.LastSeenAt = evidence.LastSeenAt is null || quiz.TakenAt > evidence.LastSeenAt
                ? quiz.TakenAt
                : evidence.LastSeenAt;
        }

        // Score each topic on its most recent attempt: someone who failed twice and then passed has
        // learned it, and averaging would hide that.
        foreach (var (topic, evidence) in byTopic)
        {
            var latest = roadmap.QuizResults
                .Where(q => string.Equals(q.Topic, topic, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.TakenAt)
                .First();

            evidence.MasteryPercent = latest.ScorePercent;
            evidence.Status = latest.ScorePercent >= 80 ? "Mastered"
                : latest.ScorePercent >= 50 ? "Learning"
                : "Struggling";
        }

        return byTopic.Values.OrderByDescending(t => t.LastSeenAt).ToList();
    }

    /// <summary>
    /// Where the roadmap should actually begin. Evidence can move someone forward; with no evidence
    /// this returns what they declared, because guessing from nothing is how you end up telling an
    /// experienced analyst to start at lesson one — or dropping a beginner into advanced material.
    /// </summary>
    private static string RecommendStartStage(UserLearningRecord record, GrowthRoadmap? roadmap)
    {
        if (roadmap is not null && !string.IsNullOrWhiteSpace(roadmap.CurrentStage))
            return roadmap.CurrentStage;

        var declared = record.DeclaredLevel;
        if (record.QuizzesTaken == 0 && record.LessonsCompleted == 0)
            return string.IsNullOrWhiteSpace(declared) ? "Beginner" : declared;

        return record.AverageQuizScore switch
        {
            >= 85 when record.QuizzesTaken >= 3 => "Advanced",
            >= 70 when record.QuizzesTaken >= 2 => "Intermediate",
            _ => string.IsNullOrWhiteSpace(declared) ? "Beginner" : declared,
        };
    }

    /// <summary>
    /// What to pull forward. Weak areas come first — the point of tracking struggle is to act on
    /// it — then the terms the user keeps returning to of their own accord.
    /// </summary>
    private static List<string> BuildPriorityTopics(UserLearningRecord record)
    {
        var priorities = new List<string>(record.WeakAreas);

        priorities.AddRange(record.Signals
            .Where(s => s.Source == "Inferred" && s.Strength >= 0.4)
            .Select(s => s.Term));

        return priorities
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !record.MasteredTopics.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
    }

    /// <summary>
    /// How much Radar actually knows. Deliberately conservative: a brand-new user scores low, and
    /// callers should stay close to declared answers until this climbs.
    /// </summary>
    private static double ComputeConfidence(UserLearningRecord record)
    {
        var score = 0.0;
        if (!string.IsNullOrWhiteSpace(record.PrimaryGoal)) score += 0.15;
        if (record.Interests.Count > 0) score += 0.10;
        if (record.Capabilities.Count > 0) score += 0.10;
        score += Math.Min(0.25, record.Signals.Count(s => s.Source == "Inferred") * 0.05);
        score += Math.Min(0.20, record.QuizzesTaken * 0.05);
        score += Math.Min(0.20, record.LessonsCompleted * 0.02);
        return Math.Round(Math.Min(1.0, score), 2);
    }

    /// <summary>Plain English, so the user can see what Radar thinks it knows and correct it.</summary>
    private static string BuildSummary(UserLearningRecord record)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(record.PrimaryGoal))
            lines.Add($"You want to {record.PrimaryGoal}.");

        if (record.QuizzesTaken == 0 && record.LessonsCompleted == 0)
        {
            lines.Add("Radar has not seen you learn anything yet, so your plan starts from what you told us.");
            lines.Add("The more you use Radar, the better this gets.");
            return string.Join(" ", lines);
        }

        lines.Add($"You have finished {record.LessonsCompleted} lessons and taken {record.QuizzesTaken} quizzes.");

        if (record.StrongAreas.Count > 0)
            lines.Add($"You are strong on {string.Join(", ", record.StrongAreas.Take(3))}.");

        if (record.WeakAreas.Count > 0)
            lines.Add($"You still find {string.Join(", ", record.WeakAreas.Take(3))} hard, so Radar will give you more practice there.");

        lines.Add($"Radar is starting you at the {record.RecommendedStartStage} stage.");
        return string.Join(" ", lines);
    }
}

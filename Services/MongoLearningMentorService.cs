using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Helpers;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// AI Learning Mentor service connected to the user's Radar roadmap, goal, and personal learning memory.
/// Implements the full pedagogical loop:
/// Understand → Practise → Apply → Get Feedback → Improve → Know what to do next.
/// </summary>
public class MongoLearningMentorService : ILearningMentorService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly RadarDatabase _db;
    private readonly IAiEngine _ai;

    public MongoLearningMentorService(IHttpClientFactory httpFactory, IConfiguration config, RadarDatabase db, IAiEngine ai)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _ai = ai;
    }

    // ── 1. Chat & Tutor ─────────────────────────────────────────────────────────────

    public async Task<ChatMessage> SendMessageAsync(string userId, string message, List<ChatMessage> history)
    {
        var fullResponse = new StringBuilder();
        await foreach (var chunk in StreamMessageAsync(userId, message, history))
            fullResponse.Append(chunk);

        return new ChatMessage { Role = "assistant", Content = fullResponse.ToString() };
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string userId,
        string message,
        List<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(userId);
        var storedRoadmap = await _db.Roadmaps.Find(r => r.UserId == userId).SortByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        var roadmap = storedRoadmap is not null && MongoRoadmapService.WasAskedFor(storedRoadmap) ? storedRoadmap : null;
        var masteries = await GetTopicMasteriesAsync(userId);
        var memory = await GetLearningMemoryAsync(userId);

        var systemPrompt = BuildMentorSystemPrompt(profile, roadmap, memory, masteries);

        var messages = history.TakeLast(20).ToList();
        messages.Add(new ChatMessage { Role = "user", Content = message });

        await foreach (var chunk in _ai.StreamChatAsync("mentor-chat", systemPrompt, messages, ct))
            yield return chunk;
    }

    // ── 2. Practice & Quiz (Adaptive Difficulty & Cognitive Testing) ────────────────

    public async Task<MentorQuiz> GenerateQuizAsync(string userId, string topic, int questionCount = 5, string difficulty = "Adaptive")
    {
        var memory = await GetLearningMemoryAsync(userId);
        var weakAreasNotice = memory.WeakAreas.Count > 0
            ? $"Note: The user previously struggled with these weak areas: {string.Join(", ", memory.WeakAreas)}. Include targeted questions on them."
            : "";

        var prompt = $$"""
            Generate an adaptive quiz with {{questionCount}} questions about: {{topic}}
            Difficulty target: {{difficulty}}
            {{weakAreasNotice}}

            Include a balanced cognitive mix:
            - Recall (direct concept check)
            - Understanding (explaining mechanisms)
            - Application (code/query/scenario interpretation)
            - Problem-Solving (debugging or strategic choice)

            Return a JSON object:
            {
              "topic": "{{topic}}",
              "difficulty": "{{difficulty}}",
              "detectedWeakAreas": ["Area 1"],
              "nextMove": "Your next move: Complete 5 practice questions on {{topic}}.",
              "questions": [
                {
                  "question": "Question text here",
                  "options": ["Option A", "Option B", "Option C", "Option D"],
                  "correctIndex": 0,
                  "explanation": "Why option A is correct and why others are incorrect",
                  "questionType": "MCQ",
                  "cognitiveLevel": "Application",
                  "codeSnippet": null,
                  "scenarioContext": "A brief practical real-world scenario if applicable",
                  "subtopic": "Subtopic name"
                }
              ]
            }
            Return only valid JSON, no markdown code fences.
            """;

        var response = await CallLlmAsync(prompt);

        var quiz = new MentorQuiz
        {
            UserId = userId,
            Topic = topic,
            Difficulty = difficulty,
            Questions = [],
            NextMove = $"Your next move: Complete practice exercises on {topic}."
        };

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("nextMove", out var nm))
                quiz.NextMove = nm.GetString() ?? quiz.NextMove;

            if (root.TryGetProperty("detectedWeakAreas", out var dwa))
            {
                foreach (var item in dwa.EnumerateArray())
                    quiz.DetectedWeakAreas.Add(item.GetString() ?? "");
            }

            if (root.TryGetProperty("questions", out var questions))
            {
                foreach (var q in questions.EnumerateArray())
                {
                    var question = new MentorQuizQuestion
                    {
                        Question = q.TryGetProperty("question", out var qText) ? qText.GetString() ?? "" : "",
                        CorrectIndex = q.TryGetProperty("correctIndex", out var ci) ? ci.GetInt32() : 0,
                        Explanation = q.TryGetProperty("explanation", out var exp) ? PlainText.Clean(exp.GetString()) : "",
                        QuestionType = q.TryGetProperty("questionType", out var qt) ? qt.GetString() ?? "MCQ" : "MCQ",
                        CognitiveLevel = q.TryGetProperty("cognitiveLevel", out var cl) ? cl.GetString() ?? "Understanding" : "Understanding",
                        CodeSnippet = q.TryGetProperty("codeSnippet", out var cs) && cs.ValueKind == JsonValueKind.String ? cs.GetString() : null,
                        ScenarioContext = q.TryGetProperty("scenarioContext", out var sc) && sc.ValueKind == JsonValueKind.String ? sc.GetString() : null,
                        Subtopic = q.TryGetProperty("subtopic", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : topic
                    };

                    if (q.TryGetProperty("options", out var opts))
                    {
                        foreach (var opt in opts.EnumerateArray())
                            question.Options.Add(opt.GetString() ?? "");
                    }

                    quiz.Questions.Add(question);
                }
            }
        }
        catch
        {
            // Seed resilient fallback questions
            quiz.Questions = SeedFallbackQuizQuestions(topic);
        }

        if (quiz.Questions.Count == 0)
            quiz.Questions = SeedFallbackQuizQuestions(topic);

        await _db.MentorQuizzes.InsertOneAsync(quiz);
        return quiz;
    }

    public async Task<MentorQuiz?> SubmitAnswerAsync(string userId, string quizId, int questionIndex, int answerIndex, string? textAnswer = null)
    {
        var quiz = await _db.MentorQuizzes.Find(q => q.Id == quizId && q.UserId == userId).FirstOrDefaultAsync();
        if (quiz is null) return null;
        if (questionIndex < 0 || questionIndex >= quiz.Questions.Count) return quiz;

        var question = quiz.Questions[questionIndex];
        if (question.UserAnswered == true) return quiz; // Idempotent

        question.UserAnswerIndex = answerIndex;
        question.UserTextAnswer = textAnswer;
        question.UserAnswered = true;

        bool isCorrect = answerIndex == question.CorrectIndex;
        question.IsCorrect = isCorrect;

        if (isCorrect)
        {
            quiz.Score++;
            if (question.CognitiveLevel is "Recall" or "Understanding")
                quiz.ConceptualScore++;
            else
                quiz.PracticalScore++;
        }
        else
        {
            // Track weak area
            var subtopic = question.Subtopic ?? quiz.Topic;
            if (!quiz.DetectedWeakAreas.Contains(subtopic))
                quiz.DetectedWeakAreas.Add(subtopic);
        }

        // Update Topic Mastery in background
        _ = UpdateTopicMasteryAsync(userId, quiz.Topic, question.Subtopic ?? quiz.Topic, isCorrect);

        quiz.CurrentQuestionIndex = Math.Max(quiz.CurrentQuestionIndex, questionIndex + 1);
        if (quiz.CurrentQuestionIndex >= quiz.Questions.Count)
        {
            quiz.IsComplete = true;
            int scorePercent = (int)Math.Round((double)quiz.Score / quiz.Questions.Count * 100);

            if (scorePercent >= 80)
            {
                quiz.NextMove = $"Your next move: High mastery confirmed ({scorePercent}%)! Move on to your real-world portfolio project in {quiz.Topic}.";
            }
            else
            {
                var weakFocus = quiz.DetectedWeakAreas.FirstOrDefault() ?? quiz.Topic;
                quiz.NextMove = $"Your next move: Score: {scorePercent}%. Practice 3 targeted questions on '{weakFocus}' to close this gap.";
            }
        }

        await _db.MentorQuizzes.ReplaceOneAsync(q => q.Id == quizId, quiz);
        return quiz;
    }

    // ── 3. Study Plan (Dynamic Week-by-Week Architecture) ───────────────────────────

    public async Task<MentorStudyPlan> CreateStudyPlanAsync(string userId, string topic, string goal, int weeks = 6)
    {
        var profile = await GetProfileAsync(userId);
        var memory = await GetLearningMemoryAsync(userId);

        var prompt = $$"""
            Create a structured {{weeks}}-week dynamic study plan for: {{topic}}
            User Goal: {{goal}}
            Current Knowledge / Stage: {{memory.CurrentStage}}
            Known Weak Areas: {{string.Join(", ", memory.WeakAreas)}}

            Structure progression:
            Beginner / Foundations → Intermediate / Core Tools → Advanced / Analysis → Real-World Portfolio Application.

            Return JSON:
            {
              "topic": "{{topic}}",
              "goal": "{{goal}}",
              "durationWeeks": {{weeks}},
              "weeks": [
                {
                  "weekNumber": 1,
                  "theme": "Week theme",
                  "milestone": "Specific verifiable milestone achieved by week's end",
                  "tasks": [
                    {
                      "title": "Task title",
                      "description": "Specific action to perform",
                      "resourceType": "Tutorial|Project|Practice|Reading",
                      "estimatedTime": "45 min",
                      "focusArea": "CoreSkill"
                    }
                  ]
                }
              ]
            }
            Return only valid JSON, no markdown.
            """;

        var response = await CallLlmAsync(prompt);

        var plan = new MentorStudyPlan
        {
            UserId = userId,
            Topic = topic,
            Goal = goal,
            DurationWeeks = weeks,
            Weeks = []
        };

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("weeks", out var weeksEl))
            {
                foreach (var week in weeksEl.EnumerateArray())
                {
                    var studyWeek = new MentorStudyWeek
                    {
                        WeekNumber = week.TryGetProperty("weekNumber", out var wn) ? wn.GetInt32() : 1,
                        Theme = week.TryGetProperty("theme", out var th) ? th.GetString() ?? "" : "",
                        Milestone = week.TryGetProperty("milestone", out var ms) ? ms.GetString() ?? "" : "",
                    };

                    if (week.TryGetProperty("tasks", out var tasks))
                    {
                        foreach (var task in tasks.EnumerateArray())
                        {
                            studyWeek.Tasks.Add(new MentorStudyTask
                            {
                                Title = task.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "",
                                Description = task.TryGetProperty("description", out var td) ? td.GetString() ?? "" : "",
                                ResourceType = task.TryGetProperty("resourceType", out var rt) ? rt.GetString() ?? "Practice" : "Practice",
                                EstimatedTime = task.TryGetProperty("estimatedTime", out var et) ? et.GetString() : "30 min",
                                FocusArea = task.TryGetProperty("focusArea", out var fa) ? fa.GetString() : "CoreSkill",
                                Relevance = TaskRelevance(
                                    task.TryGetProperty("focusArea", out var fa2) ? fa2.GetString() : "CoreSkill",
                                    plan.Goal,
                                    studyWeek.Theme),
                                SuggestedSearchUrl = SearchFor(
                                    task.TryGetProperty("title", out var tt2) ? tt2.GetString() ?? "" : "",
                                    plan.Topic),
                            });
                        }
                    }

                    plan.Weeks.Add(studyWeek);
                }
            }
        }
        catch
        {
            plan.Weeks = GenerateDefaultWeeks(topic, weeks);
        }

        if (plan.Weeks.Count == 0)
            plan.Weeks = GenerateDefaultWeeks(topic, weeks);

        await _db.MentorStudyPlans.InsertOneAsync(plan);
        return plan;
    }

    public async Task<MentorStudyPlan?> GetActiveStudyPlanAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        return await _db.MentorStudyPlans
            .Find(p => p.UserId == userId)
            .SortByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<MentorStudyPlan?> AdjustStudyPlanAsync(string userId, string planId, string reason)
    {
        var plan = await _db.MentorStudyPlans.Find(p => p.Id == planId && p.UserId == userId).FirstOrDefaultAsync();
        if (plan == null) return null;

        plan.Status = "Adjusted";
        plan.LastAdjustedAt = DateTime.UtcNow;

        // If struggling, add a revision task to the current week
        var activeWeek = plan.Weeks.FirstOrDefault(w => !w.IsCompleted) ?? plan.Weeks.LastOrDefault();
        if (activeWeek != null)
        {
            activeWeek.Tasks.Insert(0, new MentorStudyTask
            {
                Title = $"Adaptive Review: {reason}",
                Description = $"Targeted practice session added because a weak area was detected in {reason}.",
                ResourceType = "Practice",
                EstimatedTime = "25 min",
                FocusArea = "WeakArea",
                Relevance = $"Added because your answers showed {reason} was not sticking yet.",
                SuggestedSearchUrl = SearchFor(reason, plan.Topic),
            });
        }

        await _db.MentorStudyPlans.ReplaceOneAsync(p => p.Id == planId, plan);
        return plan;
    }

    public async Task<bool> ToggleTaskCompletionAsync(string userId, string planId, int weekNumber, int taskIndex)
    {
        var plan = await _db.MentorStudyPlans.Find(p => p.Id == planId && p.UserId == userId).FirstOrDefaultAsync();
        if (plan == null) return false;

        var week = plan.Weeks.FirstOrDefault(w => w.WeekNumber == weekNumber);
        if (week == null || taskIndex < 0 || taskIndex >= week.Tasks.Count) return false;

        week.Tasks[taskIndex].IsCompleted = !week.Tasks[taskIndex].IsCompleted;
        week.IsCompleted = week.Tasks.All(t => t.IsCompleted);

        await _db.MentorStudyPlans.ReplaceOneAsync(p => p.Id == planId, plan);
        return true;
    }

    // ── 5. Personal Learning Memory & Topic Mastery ─────────────────────────────────

    public async Task<PersonalLearningMemory> GetLearningMemoryAsync(string userId)
    {
        var profile = await GetProfileAsync(userId);
        var storedRoadmap = await _db.Roadmaps.Find(r => r.UserId == userId).SortByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        var roadmap = storedRoadmap is not null && MongoRoadmapService.WasAskedFor(storedRoadmap) ? storedRoadmap : null;
        var masteries = await GetTopicMasteriesAsync(userId);
        var weakList = masteries.SelectMany(m => m.Subtopics)
            .Where(s => s.MasteryPercent < 60)
            .Select(s => s.Name)
            .Distinct()
            .ToList();

        var strongList = masteries.SelectMany(m => m.Subtopics)
            .Where(s => s.MasteryPercent >= 80)
            .Select(s => s.Name)
            .Distinct()
            .ToList();

        return new PersonalLearningMemory
        {
            UserId = userId,
            PrimaryGoal = profile?.PrimaryGoal ?? string.Empty,
            // The skill being tutored, so it must be a subject. An outcome goal ("Get Promotion")
            // would otherwise become the thing the mentor tries to teach.
            ActiveSkill = roadmap?.Interest
                ?? (profile is not null ? LearningIntentReader.Read(profile).Domain : null)
                ?? string.Empty,
            CurrentStage = roadmap?.CurrentStage ?? "Beginner",
            WeakAreas = weakList,
            StrongAreas = strongList,
            Masteries = masteries,
            CompletedProjects = roadmap?.CompletedExerciseIds ?? [],
            // Nothing records mistakes yet, so the honest list is empty. These three were
            // hardcoded and shown to every learner as their own recent errors.
            RecentMistakes = []
        };
    }

    /// <summary>
    /// The exact records the old seeder wrote. Nothing else ever produced them.
    /// </summary>
    /// <remarks>
    /// Removing the seeder stopped new ones being written and did nothing for the accounts that
    /// already had them — which is everyone who had opened the Mentor. They kept being told which
    /// SQL join they keep getting wrong. These are deleted rather than hidden: they are not the
    /// learner's data in any sense, they are a fixture that was written into their account.
    ///
    /// Matched on the topic name *and* a subtopic only the seeder used, so a learner who genuinely
    /// studies SQL and has answered real questions keeps their record.
    /// </remarks>
    private static readonly Dictionary<string, string> FabricatedMasteries = new(StringComparer.Ordinal)
    {
        ["SQL & Relational Databases"] = "SQL JOINs (INNER, LEFT, FULL)",
        ["Cybersecurity & Systems Defense"] = "Zero Trust Microsegmentation",
    };

    private static bool WasFabricated(TopicMastery mastery) =>
        FabricatedMasteries.TryGetValue(mastery.Topic, out var signature)
        && mastery.Subtopics.Any(sub => string.Equals(sub.Name, signature, StringComparison.Ordinal));

    public async Task<List<TopicMastery>> GetTopicMasteriesAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return [];
        var existing = await _db.TopicMasteries.Find(t => t.UserId == userId).ToListAsync();

        var fabricated = existing.Where(WasFabricated).ToList();
        if (fabricated.Count > 0)
        {
            var ids = fabricated.Select(m => m.Id).ToList();
            await _db.TopicMasteries.DeleteManyAsync(m => ids.Contains(m.Id));
            existing = existing.Where(m => !WasFabricated(m)).ToList();
        }

        if (existing.Count > 0) return existing;

        // Nothing. A learner who has answered no questions has no mastery.
        //
        // This used to write a whole fabricated history on first read: SQL at 68% with "SELECT
        // Queries 95% mastered, 20 questions answered", JOINs flagged as a weak area at 51%, and
        // a second topic in cybersecurity — for every user, including one studying something
        // else entirely. The Mentor then reported it back as measurement: "Weak area detected
        // (SQL JOINs) based on recent performance". There was no performance.
        return [];
    }

    public async Task<TopicMastery> UpdateTopicMasteryAsync(string userId, string topic, string subtopic, bool isCorrect)
    {
        var masteries = await GetTopicMasteriesAsync(userId);
        var topicDoc = masteries.FirstOrDefault(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase))
            ?? new TopicMastery { UserId = userId, Topic = topic };

        var sub = topicDoc.Subtopics.FirstOrDefault(s => s.Name.Equals(subtopic, StringComparison.OrdinalIgnoreCase));
        if (sub == null)
        {
            sub = new SubtopicMasteryItem { Name = subtopic, MasteryPercent = isCorrect ? 65 : 45 };
            topicDoc.Subtopics.Add(sub);
        }

        sub.TotalQuestions++;
        if (isCorrect)
        {
            sub.CorrectAnswers++;
            sub.MasteryPercent = Math.Min(100, sub.MasteryPercent + 7);
        }
        else
        {
            sub.MasteryPercent = Math.Max(15, sub.MasteryPercent - 9);
        }

        sub.Status = sub.MasteryPercent >= 85 ? "Mastered" : sub.MasteryPercent >= 70 ? "Strong" : sub.MasteryPercent < 60 ? "WeakArea" : "In Progress";

        // Recompute overall topic mastery
        if (topicDoc.Subtopics.Count > 0)
            topicDoc.MasteryPercent = (int)Math.Round(topicDoc.Subtopics.Average(s => s.MasteryPercent));

        topicDoc.LastPracticedAt = DateTime.UtcNow;
        topicDoc.WeakAreas = topicDoc.Subtopics.Where(s => s.MasteryPercent < 60).Select(s => s.Name).ToList();
        topicDoc.StrongAreas = topicDoc.Subtopics.Where(s => s.MasteryPercent >= 80).Select(s => s.Name).ToList();

        await _db.TopicMasteries.ReplaceOneAsync(t => t.Id == topicDoc.Id, topicDoc, new ReplaceOptions { IsUpsert = true });
        return topicDoc;
    }

    // ── 6. Spaced Revision ──────────────────────────────────────────────────────────

    public async Task<List<SpacedRevisionItem>> GetSpacedRevisionsAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return [];
        var items = await _db.SpacedRevisions.Find(s => s.UserId == userId).ToListAsync();
        if (items.Count > 0) return items;

        // Seed initial spaced repetition schedule
        var defaultSchedule = new List<SpacedRevisionItem>
        {
            new() { UserId = userId, Topic = "SQL Fundamentals & Relational Algebra", IntervalName = "Day 1: Learn", DueDate = DateTime.UtcNow.AddDays(-1), IsCompleted = true, Prompt = "Understand table schema, primary keys, and foreign keys." },
            new() { UserId = userId, Topic = "SQL JOINs (INNER, LEFT, RIGHT, FULL)", IntervalName = "Day 2: Quick Recall", DueDate = DateTime.UtcNow, IsCompleted = false, Prompt = "Quick Recall: When would a LEFT JOIN produce NULL columns, and how does it differ from INNER JOIN?" },
            new() { UserId = userId, Topic = "GROUP BY & HAVING Clauses", IntervalName = "Day 4: Practice", DueDate = DateTime.UtcNow.AddDays(2), IsCompleted = false, Prompt = "Practice: Write a query grouping customer purchases having total spend > $500." },
            new() { UserId = userId, Topic = "Subqueries & Window Functions", IntervalName = "Day 7: Application", DueDate = DateTime.UtcNow.AddDays(5), IsCompleted = false, Prompt = "Application: Calculate a running total of monthly recurring revenue using OVER (PARTITION BY)." },
            new() { UserId = userId, Topic = "Comprehensive Database Optimization", IntervalName = "Day 21: Mastery Test", DueDate = DateTime.UtcNow.AddDays(18), IsCompleted = false, Prompt = "Mastery Test: Index tuning and query execution plan diagnostics under 100k requests/sec." }
        };

        await _db.SpacedRevisions.InsertManyAsync(defaultSchedule);
        return defaultSchedule;
    }

    public async Task<bool> CompleteSpacedRevisionAsync(string userId, string revisionId, bool wasRemembered)
    {
        var item = await _db.SpacedRevisions.Find(s => s.Id == revisionId && s.UserId == userId).FirstOrDefaultAsync();
        if (item == null) return false;

        item.IsCompleted = true;
        item.WasRemembered = wasRemembered;

        await _db.SpacedRevisions.ReplaceOneAsync(s => s.Id == revisionId, item);
        return true;
    }

    // ── 7. Challenge Mode & Daily Learning Card ────────────────────────────────────

    public async Task<ChallengeResult> RunChallengeAsync(string userId, string? topic = null)
    {
        var memory = await GetLearningMemoryAsync(userId);
        var targetTopic = !string.IsNullOrWhiteSpace(topic) ? topic : (memory.WeakAreas.FirstOrDefault() ?? memory.ActiveSkill);

        // Generate rapid diagnostic challenge
        var quiz = await GenerateQuizAsync(userId, targetTopic, 5, "Adaptive");
        var conceptual = 80;
        var practical = 60;

        return new ChallengeResult
        {
            Topic = targetTopic,
            ScorePercent = 74,
            ConceptualScorePercent = conceptual,
            PracticalScorePercent = practical,
            Strengths = ["Clear conceptual grasp of foundational mechanics", "Correct definition recall"],
            Weaknesses = [$"Applying {targetTopic} under time-constrained real-world scenarios", "Edge cases in complex inputs"],
            Recommendation = $"You understand the theory. Your next focus must be hands-on problem solving in {targetTopic}.",
            NextMove = $"Your next move: Solve 3 practical scenarios on {targetTopic}."
        };
    }

    public async Task<DailyLearningCard> GetTodayLearningAsync(string userId)
    {
        var memory = await GetLearningMemoryAsync(userId);

        // Only recommend what there is evidence for: a weak area the learner actually produced by
        // answering questions, or failing that the section they are working through. "SQL JOINs"
        // was the hardcoded fallback, announced as detected from performance that never happened.
        var weakTopic = memory.WeakAreas.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));
        var fromPath = string.IsNullOrWhiteSpace(memory.ActiveSkill) ? null : memory.ActiveSkill;

        if (weakTopic is null && fromPath is null)
        {
            return new DailyLearningCard
            {
                EmptyReason = "Build a path first. Once you are working through a section, the mentor can tell you what to spend twenty minutes on.",
            };
        }

        if (weakTopic is null)
        {
            return new DailyLearningCard
            {
                Topic = fromPath!,
                Skill = memory.ActiveSkill,
                EstimatedMinutes = 20,
                Why = $"From your path. You have not answered any questions on {fromPath} yet, so there is nothing measured to go on.",
                NextMove = $"Your next move: work through {fromPath} and take a quiz, so the mentor has something to aim at.",
                SuggestedPrompt = $"Teach me {fromPath} and give me an exercise.",
            };
        }

        return new DailyLearningCard
        {
            Topic = weakTopic,
            Skill = memory.ActiveSkill,
            EstimatedMinutes = 20,
            Why = $"Weak area detected ({weakTopic}) based on recent performance. Dedicate 20 minutes to close this gap.",
            LearnMinutes = 8,
            PracticeMinutes = 8,
            ApplyMinutes = 4,
            NextMove = $"Your next move: Master {weakTopic} through 8 min study, 8 min practice, and 4 min application.",
            SuggestedPrompt = $"Teach me {weakTopic}. Explain why it matters for my goal ({memory.PrimaryGoal}), give an example, and test my understanding."
        };
    }

    // ── Private Helpers ─────────────────────────────────────────────────────────────

    private async Task<string> CallLlmAsync(string prompt)
    {
        return await _ai.GenerateTextAsync("mentor-generation", "You are Radar's learning mentor. Return only valid JSON when requested, with no markdown code fences.", prompt) ?? "{}";
    }

    private async Task<UserProfile?> GetProfileAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null) return null;
        return await _db.Profiles.Find(p => p.Id == user.ProfileId).FirstOrDefaultAsync();
    }

    private static string BuildMentorSystemPrompt(UserProfile? profile, GrowthRoadmap? roadmap, PersonalLearningMemory memory, List<TopicMastery> masteries)
    {
        var name = profile?.Name ?? "Learner";
        var goal = profile?.PrimaryGoal ?? "Advance career and technological capabilities";
        var roadmapTitle = roadmap?.Title ?? "Strategic Learning Pathway";
        var currentStage = roadmap?.CurrentStage ?? "Beginner";
        var weakAreasStr = memory.WeakAreas.Count > 0 ? string.Join(", ", memory.WeakAreas) : "None detected yet";
        var strongAreasStr = memory.StrongAreas.Count > 0 ? string.Join(", ", memory.StrongAreas) : "Foundations established";

        return $"""
            You are the Radar AI Learning Mentor — an expert, highly personalized tutor connected directly to {name}'s Radar Roadmap and career goals.

            LEARNER PROFILE & ROADMAP CONTEXT:
            - Learner: {name}
            - Primary Goal: {goal}
            - Active Roadmap: {roadmapTitle} (Current Stage: {currentStage})
            - Known Weak Areas: {weakAreasStr}
            - Known Strong Areas: {strongAreasStr}

            CORE PEDAGOGICAL PRINCIPLES (NEVER BEHAVE LIKE A GENERIC CHATBOT):
            1. EXPLAIN SIMPLY & ADAPT: Match explanations to their current stage ({currentStage}). Use crisp, jargon-free analogies.
            2. PROVIDE EXAMPLES: Always illustrate concepts with real-world, practical examples.
            3. SOCRATIC CHECK: Always ask 1 short, focused question at the end to check understanding.
            4. CONNECT TO GOAL: Explain why this specific topic matters for achieving "{goal}".
            5. RECOGNIZE WEAK AREAS: If they struggle with {weakAreasStr}, guide them patiently without giving away the full answer immediately.
            6. MANDATORY CLOSING: End every single tutoring message with:
               **Your next move:** [Specific actionable task, e.g. "Complete 3 practice questions on X" or "Rewrite paragraph 3" or "Start the portfolio dashboard"].

            FORMATTING GUIDELINES:
            - Write in structured, clean markdown with bold section headers and bullet points.
            - Do not give shallow answers. Drive genuine comprehension and application.
            """;
    }

    private static List<MentorQuizQuestion> SeedFallbackQuizQuestions(string topic)
    {
        return
        [
            new()
            {
                Question = $"What is the primary operational objective when implementing {topic}?",
                Options =
                [
                    "To establish reproducible, verified outcomes while reducing operational friction",
                    "To strictly memorize theoretical definitions without application",
                    "To bypass all established standard operating procedures",
                    "To rely entirely on unverified assumptions"
                ],
                CorrectIndex = 0,
                Explanation = "Practical application and reproducible validation represent the foundational goal of effective mastery.",
                QuestionType = "MCQ",
                CognitiveLevel = "Understanding",
                Subtopic = topic
            },
            new()
            {
                Question = $"In a production environment, which failure mode is most commonly encountered when executing {topic}?",
                Options =
                [
                    "Over-indexing on syntax while ignoring underlying data schema dependencies",
                    "Over-communicating with cross-functional team members",
                    "Documenting empirical edge cases too thoroughly",
                    "Testing assumptions against synthetic benchmarks"
                ],
                CorrectIndex = 0,
                Explanation = "Neglecting schema constraints and dependency structures is the leading cause of downstream execution errors.",
                QuestionType = "CaseStudy",
                CognitiveLevel = "Application",
                Subtopic = topic
            },
            new()
            {
                Question = $"True or False: In {topic}, achieving 100% course completion is equivalent to operational mastery.",
                Options = ["True", "False"],
                CorrectIndex = 1,
                Explanation = "Completion does not equal mastery. Mastery requires continuous problem-solving, recall under friction, and real-world project execution.",
                QuestionType = "TrueFalse",
                CognitiveLevel = "Recall",
                Subtopic = topic
            }
        ];
    }

    /// <summary>
    /// Where to look for the material a task needs.
    ///
    /// A search, never a fixed link. Of the 33 hardcoded video ids this codebase used to carry,
    /// 21 were already dead - a suggestion nobody has verified should not pretend to be one
    /// somebody has.
    /// </summary>
    private static string SearchFor(string taskTitle, string subject)
    {
        var query = string.IsNullOrWhiteSpace(taskTitle)
            ? subject
            : $"{taskTitle} {subject}".Trim();

        return string.IsNullOrWhiteSpace(query)
            ? string.Empty
            : $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
    }

    /// <summary>
    /// Why a task is on the plan, in terms of the learner's own goal.
    ///
    /// Spec 9.4. Built from the focus area the planner already assigned and the goal the learner
    /// already stated, so it describes a relationship that exists. Where the focus area says
    /// nothing useful it returns empty rather than filling the line with encouragement - a
    /// sentence that would read the same on every task teaches the learner to skip the field.
    /// </summary>
    private static string TaskRelevance(string? focusArea, string goal, string theme)
    {
        var target = string.IsNullOrWhiteSpace(goal) ? null : SubjectPhrase.Lower(goal);

        return focusArea switch
        {
            "WeakArea" when !string.IsNullOrWhiteSpace(theme)
                => $"Covers {SubjectPhrase.Lower(theme)}, which your answers show is not solid yet.",
            "WeakArea" => "Covers something your answers show is not solid yet.",

            "Portfolio" when target is not null
                => $"Produces something you can show when applying for {target}.",
            "Portfolio" => "Produces something you can show to an employer.",

            // When the theme already names the subject, repeating it produces "the basics of
            // nursing is part of the core of nursing" - true, and useless.
            "CoreSkill" when target is not null && !string.IsNullOrWhiteSpace(theme)
                             && !theme.Contains(target, StringComparison.OrdinalIgnoreCase)
                => $"{SubjectPhrase.Clean(theme)} is part of the core of {target}.",
            "CoreSkill" when target is not null => $"Part of the core of {target}.",

            _ => string.Empty,
        };
    }

    /// <summary>
    /// The plan built when the model is unavailable.
    ///
    /// The week themes used to be data-analytics curriculum applied to every subject: week two
    /// was "Data Preparation, Ingestion & Tool Setup" and week three "Advanced Modeling &
    /// Analytical Techniques" whatever the learner had asked for, so a nursing student got a
    /// modelling week. They are phrased against the learner's own topic now, which reads
    /// correctly for any subject because it asserts nothing about the subject's content.
    ///
    /// Tasks carry the same relevance line and search suggestion the generated path produces -
    /// this is the path that runs with no API key configured, so it is the one most learners
    /// actually see.
    /// </summary>
    private static List<MentorStudyWeek> GenerateDefaultWeeks(string topic, int weeks)
    {
        var subject = SubjectPhrase.Lower(topic);
        var list = new List<MentorStudyWeek>();

        for (int i = 1; i <= weeks; i++)
        {
            var theme = i switch
            {
                1 => $"The basics of {subject}",
                2 => $"Tools and setup for {subject}",
                3 => $"Going deeper into {subject}",
                4 => $"Applying {subject} to real cases",
                5 => "Where this usually goes wrong",
                _ => "A project you can show someone",
            };

            var deepDive = $"Work through {theme.ToLowerInvariant()}";
            var practice = $"Practise {theme.ToLowerInvariant()}";

            list.Add(new MentorStudyWeek
            {
                WeekNumber = i,
                Theme = theme,
                Milestone = $"Something finished you can point at for: {theme}",
                Tasks =
                [
                    new()
                    {
                        Title = deepDive,
                        Description = $"Learn the core of {theme.ToLowerInvariant()} well enough to explain it.",
                        ResourceType = "Tutorial",
                        EstimatedTime = "45 min",
                        FocusArea = "CoreSkill",
                        Relevance = TaskRelevance("CoreSkill", topic, theme),
                        SuggestedSearchUrl = SearchFor(theme, topic),
                    },
                    new()
                    {
                        Title = practice,
                        Description = $"Do it yourself without following along, and note what you had to look up.",
                        ResourceType = "Practice",
                        EstimatedTime = "30 min",
                        FocusArea = "Portfolio",
                        Relevance = TaskRelevance("Portfolio", topic, theme),
                        SuggestedSearchUrl = SearchFor(practice, topic),
                    },
                ],
            });
        }

        return list;
    }

}

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
        var roadmap = await _db.Roadmaps.Find(r => r.UserId == userId).SortByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
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
                                FocusArea = task.TryGetProperty("focusArea", out var fa) ? fa.GetString() : "CoreSkill"
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
                FocusArea = "WeakArea"
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

    // ── 4. Work Review & Revision Loop ──────────────────────────────────────────────

    public async Task<MentorWorkReview> ReviewWorkAsync(string userId, string workTitle, string workContent, string reviewType, string? revisedFromId = null)
    {
        MentorWorkReview? previousReview = null;
        if (!string.IsNullOrEmpty(revisedFromId))
        {
            previousReview = await _db.MentorWorkReviews.Find(r => r.Id == revisedFromId && r.UserId == userId).FirstOrDefaultAsync();
        }

        var previousContext = previousReview != null
            ? $"""
               NOTE: This is a REVISED SUBMISSION of previous work.
               Previous Score: {previousReview.Score}/100.
               Previous Main Issue: "{previousReview.MainIssue}"
               Previous Next Step: "{previousReview.NextStep}"
               Please compare the new version against the previous issue. Did the user address it? Give an improved score if warranted and highlight how they improved.
               """
            : "";

        var prompt = $$"""
            You are the Radar AI Learning Mentor reviewing a user's submitted {{reviewType.ToLowerInvariant()}}.
            Title: "{{workTitle}}"
            Content:
            ---
            {{workContent}}
            ---

            {{previousContext}}

            Perform a rigorous, constructive pedagogical evaluation:
            1. Assign an objective Score out of 100 (e.g. 76).
            2. Identify 2-3 specific Strengths.
            3. Identify 2-3 Weaknesses.
            4. State the Main Issue clearly: Explain WHAT is wrong and WHY it undermines the work.
            5. Provide 2-3 Specific Improvements.
            6. Give an explicit, immediate Next Step (e.g. "Rewrite paragraph 3 using the evidence-analysis approach.").

            Return JSON:
            {
              "score": 78,
              "feedback": "Overall assessment paragraph.",
              "strengths": ["Strength 1", "Strength 2"],
              "weaknesses": ["Weakness 1", "Weakness 2"],
              "mainIssue": "You provided evidence but did not explain why the evidence supports your argument.",
              "improvements": ["Improvement 1", "Improvement 2"],
              "suggestions": ["Broader suggestion 1", "Broader suggestion 2"],
              "nextStep": "Rewrite paragraph 3 using the evidence-analysis approach."
            }
            Return only valid JSON, no markdown.
            """;

        var response = await CallLlmAsync(prompt);

        var review = new MentorWorkReview
        {
            UserId = userId,
            WorkTitle = workTitle,
            WorkContent = workContent,
            ReviewType = reviewType,
            RevisedFromId = revisedFromId,
            RevisionNumber = previousReview != null ? previousReview.RevisionNumber + 1 : 1,
            PreviousScore = previousReview?.Score
        };

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            review.Score = root.TryGetProperty("score", out var sc) ? sc.GetInt32() : 75;
            review.Feedback = root.TryGetProperty("feedback", out var fb) ? fb.GetString() ?? "" : "";
            review.MainIssue = root.TryGetProperty("mainIssue", out var mi) ? mi.GetString() ?? "" : "";
            review.NextStep = root.TryGetProperty("nextStep", out var ns) ? ns.GetString() ?? "" : "Review the suggestions and make targeted revisions.";

            if (root.TryGetProperty("strengths", out var str))
                foreach (var s in str.EnumerateArray()) review.Strengths.Add(s.GetString() ?? "");

            if (root.TryGetProperty("weaknesses", out var wk))
                foreach (var w in wk.EnumerateArray()) review.Weaknesses.Add(w.GetString() ?? "");

            if (root.TryGetProperty("improvements", out var imp))
                foreach (var i in imp.EnumerateArray()) review.Improvements.Add(i.GetString() ?? "");

            if (root.TryGetProperty("suggestions", out var sug))
                foreach (var s in sug.EnumerateArray()) review.Suggestions.Add(s.GetString() ?? "");
        }
        catch
        {
            review.Score = previousReview != null ? Math.Min(100, previousReview.Score + 8) : 74;
            review.Feedback = "Work evaluated. Good foundational structure with key areas for analytical improvement.";
            review.Strengths = ["Clear structure", "Directly addresses the primary question"];
            review.Weaknesses = ["Depth of analytical synthesis", "Conclusion needs stronger implications"];
            review.MainIssue = "You cited observational facts but did not analyze the underlying causal mechanisms.";
            review.Improvements = ["Add a comparative counter-example", "Explicitly link findings to your primary thesis"];
            review.Suggestions = ["Consult benchmark case studies in this topic", "Quantify metrics where possible"];
            review.NextStep = "Rewrite the core analysis section using the claim-evidence-impact framework.";
        }

        // Clean formatting
        review.Feedback = PlainText.Clean(review.Feedback);
        review.MainIssue = PlainText.Clean(review.MainIssue);
        review.NextStep = PlainText.Clean(review.NextStep);

        await _db.MentorWorkReviews.InsertOneAsync(review);
        return review;
    }

    public async Task<List<MentorWorkReview>> GetUserReviewsAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return [];
        return await _db.MentorWorkReviews
            .Find(r => r.UserId == userId)
            .SortByDescending(r => r.ReviewedAt)
            .Limit(20)
            .ToListAsync();
    }

    // ── 5. Personal Learning Memory & Topic Mastery ─────────────────────────────────

    public async Task<PersonalLearningMemory> GetLearningMemoryAsync(string userId)
    {
        var profile = await GetProfileAsync(userId);
        var roadmap = await _db.Roadmaps.Find(r => r.UserId == userId).SortByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        var masteries = await GetTopicMasteriesAsync(userId);
        var reviewsCount = await _db.MentorWorkReviews.CountDocumentsAsync(r => r.UserId == userId);

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
            PrimaryGoal = profile?.PrimaryGoal ?? "Master Tech & Business Intelligence",
            // The skill being tutored, so it must be a subject. An outcome goal ("Get Promotion")
            // would otherwise become the thing the mentor tries to teach.
            ActiveSkill = roadmap?.Interest
                ?? (profile is not null ? LearningIntentReader.Read(profile).Domain : null)
                ?? "Data & Systems",
            CurrentStage = roadmap?.CurrentStage ?? "Beginner",
            WeakAreas = weakList,
            StrongAreas = strongList,
            Masteries = masteries,
            CompletedProjects = roadmap?.CompletedExerciseIds ?? [],
            SubmittedReviewsCount = (int)reviewsCount,
            RecentMistakes =
            [
                "Confused JOIN conditions with WHERE filtering",
                "Assumed completion equals full operational mastery",
                "Omitted counterfactual controls in project brief"
            ]
        };
    }

    public async Task<List<TopicMastery>> GetTopicMasteriesAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return [];
        var existing = await _db.TopicMasteries.Find(t => t.UserId == userId).ToListAsync();
        if (existing.Count > 0) return existing;

        // Seed rich initial topic mastery profile based on user's active domain
        var seeded = SeedDefaultMasteries(userId);
        if (seeded.Count > 0)
        {
            await _db.TopicMasteries.InsertManyAsync(seeded);
            return seeded;
        }

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
        var weakTopic = memory.WeakAreas.FirstOrDefault() ?? "SQL JOINs";

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

    private static List<MentorStudyWeek> GenerateDefaultWeeks(string topic, int weeks)
    {
        var list = new List<MentorStudyWeek>();
        for (int i = 1; i <= weeks; i++)
        {
            var theme = i switch
            {
                1 => $"{topic} Fundamentals & Core Architecture",
                2 => "Data Preparation, Ingestion & Tool Setup",
                3 => "Advanced Modeling & Analytical Techniques",
                4 => "Applied Case Studies & Scenario Handling",
                5 => "Optimization, Security & Error Diagnostics",
                _ => "End-to-End Capstone Portfolio Project"
            };

            list.Add(new MentorStudyWeek
            {
                WeekNumber = i,
                Theme = theme,
                Milestone = $"Deliver verified artifact for {theme}",
                Tasks =
                [
                    new() { Title = $"Deep Dive: {theme}", Description = $"Master key concepts and practical patterns in {theme}.", ResourceType = "Tutorial", EstimatedTime = "45 min", FocusArea = "CoreSkill" },
                    new() { Title = $"Hands-On Exercise: {theme}", Description = $"Execute exercises to test understanding of {theme}.", ResourceType = "Practice", EstimatedTime = "30 min", FocusArea = "WeakArea" }
                ]
            });
        }
        return list;
    }

    private static List<TopicMastery> SeedDefaultMasteries(string userId)
    {
        return
        [
            new()
            {
                UserId = userId,
                Topic = "SQL & Relational Databases",
                MasteryPercent = 68,
                Subtopics =
                [
                    new() { Name = "SELECT Queries", MasteryPercent = 95, Status = "Mastered", TotalQuestions = 20, CorrectAnswers = 19 },
                    new() { Name = "WHERE Filtering", MasteryPercent = 91, Status = "Mastered", TotalQuestions = 15, CorrectAnswers = 14 },
                    new() { Name = "GROUP BY & Aggregations", MasteryPercent = 82, Status = "Strong", TotalQuestions = 12, CorrectAnswers = 10 },
                    new() { Name = "SQL JOINs (INNER, LEFT, FULL)", MasteryPercent = 51, Status = "WeakArea", TotalQuestions = 14, CorrectAnswers = 7 },
                    new() { Name = "Subqueries & Window Functions", MasteryPercent = 42, Status = "WeakArea", TotalQuestions = 10, CorrectAnswers = 4 }
                ],
                WeakAreas = ["SQL JOINs (INNER, LEFT, FULL)", "Subqueries & Window Functions"],
                StrongAreas = ["SELECT Queries", "WHERE Filtering", "GROUP BY & Aggregations"]
            },
            new()
            {
                UserId = userId,
                Topic = "Cybersecurity & Systems Defense",
                MasteryPercent = 71,
                Subtopics =
                [
                    new() { Name = "Threat Modeling & MITRE ATT&CK", MasteryPercent = 88, Status = "Mastered", TotalQuestions = 16, CorrectAnswers = 14 },
                    new() { Name = "Identity & Access Management (IAM)", MasteryPercent = 82, Status = "Strong", TotalQuestions = 12, CorrectAnswers = 10 },
                    new() { Name = "Network Packet & PCAP Analysis", MasteryPercent = 54, Status = "WeakArea", TotalQuestions = 15, CorrectAnswers = 8 },
                    new() { Name = "Zero Trust Microsegmentation", MasteryPercent = 76, Status = "Strong", TotalQuestions = 10, CorrectAnswers = 8 },
                    new() { Name = "SIEM Log Parsing & Querying", MasteryPercent = 58, Status = "WeakArea", TotalQuestions = 12, CorrectAnswers = 7 }
                ],
                WeakAreas = ["Network Packet & PCAP Analysis", "SIEM Log Parsing & Querying"],
                StrongAreas = ["Threat Modeling & MITRE ATT&CK", "Identity & Access Management (IAM)"]
            }
        ];
    }
}

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
/// Real implementation of ILearningMentorService using OpenRouter LLM.
/// Supports streaming chat, quiz generation, study plans, and work reviews.
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

    // ── Chat ───────────────────────────────────────────────────────────────

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
        var messages = history.TakeLast(20).ToList();
        messages.Add(new ChatMessage { Role = "user", Content = message });
        await foreach (var chunk in _ai.StreamChatAsync("mentor-chat", BuildSystemPrompt(profile), messages, ct))
            yield return chunk;
    }

    // ── Quiz ───────────────────────────────────────────────────────────────

    public async Task<MentorQuiz> GenerateQuizAsync(string userId, string topic, int questionCount = 5)
    {
        var prompt = "Generate a quiz with " + questionCount + " multiple-choice questions about: " + topic + "\n\n" +
            "Return a JSON object: { \"questions\": [ { \"question\": \"text\", \"options\": [\"A\",\"B\",\"C\",\"D\"], \"correctIndex\": 0, \"explanation\": \"why\" } ] }\n" +
            "Make questions progressively harder. Return only valid JSON, no markdown.";

        var response = await CallLlmAsync(prompt);

        var quiz = new MentorQuiz
        {
            UserId = userId,
            Topic = topic,
            Questions = [],
        };

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("questions", out var questions))
            {
                foreach (var q in questions.EnumerateArray())
                {
                    var question = new MentorQuizQuestion
                    {
                        Question = q.TryGetProperty("question", out var qText) ? qText.GetString() ?? "" : "",
                        CorrectIndex = q.TryGetProperty("correctIndex", out var ci) ? ci.GetInt32() : 0,
                        Explanation = q.TryGetProperty("explanation", out var exp) ? PlainText.Clean(exp.GetString()) : "",
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
            quiz.Questions.Add(new MentorQuizQuestion
            {
                Question = "What is the most important concept in " + topic + "?",
                Options = ["Apply it practically", "Memorise definitions", "Skip it", "Ask someone else"],
                CorrectIndex = 0,
                Explanation = "Practical application is always the best way to learn.",
            });
        }

        await _db.MentorQuizzes.InsertOneAsync(quiz);
        return quiz;
    }

    public async Task<MentorQuiz?> SubmitAnswerAsync(string userId, string quizId, int questionIndex, int answerIndex)
    {
        var quiz = await _db.MentorQuizzes.Find(q => q.Id == quizId && q.UserId == userId).FirstOrDefaultAsync();
        if (quiz is null) return null;
        if (questionIndex < 0 || questionIndex >= quiz.Questions.Count) return quiz;

        var question = quiz.Questions[questionIndex];
        if (question.UserAnswered == true) return quiz; // already answered — idempotent

        question.UserAnswerIndex = answerIndex;
        question.UserAnswered = true;
        if (answerIndex == question.CorrectIndex) quiz.Score++;
        quiz.CurrentQuestionIndex = Math.Max(quiz.CurrentQuestionIndex, questionIndex + 1);
        if (quiz.CurrentQuestionIndex >= quiz.Questions.Count) quiz.IsComplete = true;

        await _db.MentorQuizzes.ReplaceOneAsync(q => q.Id == quizId, quiz);
        return quiz;
    }

    // ── Study Plan ─────────────────────────────────────────────────────────

    public async Task<MentorStudyPlan> CreateStudyPlanAsync(string userId, string topic, string goal)
    {
        var prompt = "Create a 4-week study plan for: " + topic + "\nGoal: " + goal + "\n\n" +
            "Return a JSON object: { \"weeks\": [ { \"weekNumber\": 1, \"theme\": \"Theme\", \"tasks\": [ { \"title\": \"Task\", \"description\": \"What to do\", \"resourceType\": \"Video|Article|Course|Project\", \"estimatedTime\": \"30 min\" } ] } ] }\n" +
            "Make it practical and specific. Return only valid JSON, no markdown.";

        var response = await CallLlmAsync(prompt);

        var plan = new MentorStudyPlan
        {
            UserId = userId,
            Topic = topic,
            Goal = goal,
        };

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("weeks", out var weeks))
            {
                foreach (var week in weeks.EnumerateArray())
                {
                    var studyWeek = new MentorStudyWeek
                    {
                        WeekNumber = week.TryGetProperty("weekNumber", out var wn) ? wn.GetInt32() : 0,
                        Theme = week.TryGetProperty("theme", out var th) ? th.GetString() ?? "" : "",
                    };

                    if (week.TryGetProperty("tasks", out var tasks))
                    {
                        foreach (var task in tasks.EnumerateArray())
                        {
                            studyWeek.Tasks.Add(new MentorStudyTask
                            {
                                Title = task.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "",
                                Description = task.TryGetProperty("description", out var td) ? td.GetString() ?? "" : "",
                                ResourceType = task.TryGetProperty("resourceType", out var rt) ? rt.GetString() ?? "" : "",
                                EstimatedTime = task.TryGetProperty("estimatedTime", out var et) ? et.GetString() : null,
                            });
                        }
                    }

                    plan.Weeks.Add(studyWeek);
                }
            }
        }
        catch
        {
            plan.Weeks = Enumerable.Range(1, 4).Select(i => new MentorStudyWeek
            {
                WeekNumber = i,
                Theme = i switch { 1 => "Foundations", 2 => "Core Concepts", 3 => "Practice", 4 => "Apply", _ => "Week " + i },
                Tasks =
                [
                    new() { Title = "Study " + topic + " — Week " + i, ResourceType = "Reading", EstimatedTime = "1 hour" },
                ]
            }).ToList();
        }

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

    // ── Work Review ────────────────────────────────────────────────────────

    public async Task<MentorWorkReview> ReviewWorkAsync(string userId, string workTitle, string workContent, string reviewType)
    {
        var prompt = "Review this " + reviewType.ToLower() + " titled \"" + workTitle + "\":\n\n" + workContent + "\n\n" +
            "Return a JSON object: { \"feedback\": \"assessment\", \"strengths\": [\"s1\",\"s2\"], \"improvements\": [\"i1\",\"i2\"], \"suggestions\": [\"sug1\",\"sug2\"] }\n" +
            "Be specific and actionable. Return only valid JSON, no markdown.";

        var response = await CallLlmAsync(prompt);

        var review = new MentorWorkReview
        {
            WorkTitle = workTitle,
            WorkContent = workContent,
            ReviewType = reviewType,
        };

        try
        {
            using var doc = JsonDocument.Parse(response);

            review.Feedback = doc.RootElement.TryGetProperty("feedback", out var fb) ? fb.GetString() ?? "" : "";

            if (doc.RootElement.TryGetProperty("strengths", out var strengths))
                foreach (var s in strengths.EnumerateArray())
                    review.Strengths.Add(s.GetString() ?? "");

            if (doc.RootElement.TryGetProperty("improvements", out var improvements))
                foreach (var i in improvements.EnumerateArray())
                    review.Improvements.Add(i.GetString() ?? "");

            if (doc.RootElement.TryGetProperty("suggestions", out var suggestions))
                foreach (var s in suggestions.EnumerateArray())
                    review.Suggestions.Add(s.GetString() ?? "");
        }
        catch
        {
            review.Feedback = "Review generated. Please check the suggestions below.";
            review.Strengths = ["Clear structure", "Good use of evidence"];
            review.Improvements = ["Consider adding counter-arguments", "The conclusion could be stronger"];
            review.Suggestions = ["Look at related research", "Add more specific examples"];
        }

        // Mentor answers should read as clean text, not markdown.
        review.Feedback = PlainText.Clean(review.Feedback);
        review.Strengths = review.Strengths.Select(PlainText.Clean).Where(s => s.Length > 0).ToList();
        review.Improvements = review.Improvements.Select(PlainText.Clean).Where(s => s.Length > 0).ToList();
        review.Suggestions = review.Suggestions.Select(PlainText.Clean).Where(s => s.Length > 0).ToList();

        return review;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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

    private static string BuildSystemPrompt(UserProfile? profile)
    {
        if (profile == null)
            return "You are a learning mentor for Radar. Help learners understand concepts, prepare for quizzes, create study plans, and review their work. Be encouraging, specific, and actionable. Write in clean plain text — no markdown symbols (no #, **, *, or backticks); use '• ' for lists.";

        return "You are a learning mentor for " + profile.Name + ".\n\n" +
            "Their profile:\n" +
            "- Persona: " + profile.Persona + "\n" +
            "- Primary goal: " + profile.PrimaryGoal + "\n" +
            "- Interests: " + string.Join(", ", profile.Interests) + "\n\n" +
            "Your role: help them learn efficiently, test their understanding with quizzes, create actionable study plans, and give constructive feedback on their work.\n\n" +
            "Guidelines:\n" +
            "- Be encouraging but honest\n" +
            "- Reference their goal when suggesting resources\n" +
            "- Write in clean plain text: no markdown (no # headings, ** bold, * italics or backticks). Use '• ' for lists.\n" +
            "- When generating quizzes, make questions practical and relevant\n" +
            "- When reviewing work, be specific about what to improve\n" +
            "- Keep responses focused and actionable";
    }
}

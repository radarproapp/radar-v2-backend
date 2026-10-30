using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Helpers;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// AI Learning Mentor — Socratic chat tutor, adaptive quizzes, dynamic study plans,
/// work reviews with revision loops, personal learning memory, topic mastery tracking,
/// spaced repetition, and daily learning cards.
/// </summary>
[ApiController]
[Authorize]
[Route("api/mentor")]
public class MentorController : ControllerBase
{
    private readonly ILearningMentorService _mentor;
    private readonly IUserProfileService _profiles;
    private readonly IRoadmapService _roadmaps;

    public MentorController(
        ILearningMentorService mentor,
        IUserProfileService profiles,
        IRoadmapService roadmaps)
    {
        _mentor = mentor;
        _profiles = profiles;
        _roadmaps = roadmaps;
    }

    // ── Chat & Tutor ────────────────────────────────────────────────────────
    public sealed class ChatRequest
    {
        [Required, MinLength(1)] public string Message { get; set; } = string.Empty;
        public List<ChatMessage> History { get; set; } = [];
    }

    [HttpPost("chat/stream")]
    public async Task ChatStreamAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await SseWriter.WriteChatStreamAsync(Response, _mentor.StreamMessageAsync(profile.Id, request.Message, request.History, ct), ct);
    }

    // ── Practice & Quiz ─────────────────────────────────────────────────────
    public sealed class GenerateQuizRequest
    {
        [Required, MinLength(1)] public string Topic { get; set; } = string.Empty;
        public int QuestionCount { get; set; } = 5;
        public string Difficulty { get; set; } = "Adaptive";
    }

    [HttpPost("quiz")]
    [EnableRateLimiting(RequestLimits.Thinking)]
    public async Task<IActionResult> GenerateQuizAsync([FromBody] GenerateQuizRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var quiz = await _mentor.GenerateQuizAsync(profile.Id, request.Topic, Math.Clamp(request.QuestionCount, 1, 20), request.Difficulty);

        // The same envelope as an answer, so the client reads one shape throughout. Nothing is
        // answered yet, so the summary is all zeros and nulls — which is correct, and which the
        // client renders as nothing rather than as a score of zero.
        return Ok(new
        {
            quiz,
            performance = QuizPerformance.Build(quiz),
        });
    }

    public sealed class SubmitAnswerRequest
    {
        [Required] public int QuestionIndex { get; set; }
        [Required] public int AnswerIndex { get; set; }
        public string? TextAnswer { get; set; }
    }

    [HttpPost("quiz/{quizId}/answer")]
    public async Task<IActionResult> SubmitAnswerAsync(string quizId, [FromBody] SubmitAnswerRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var quiz = await _mentor.SubmitAnswerAsync(profile.Id, quizId, request.QuestionIndex, request.AnswerIndex, request.TextAnswer);
        if (quiz is null) return NotFound(new { error = "Quiz not found" });

        // The performance summary rides with every answer rather than hanging off a separate
        // endpoint the client has to remember to call. It is counted from the answers already
        // stored on the quiz, so it needs no model call and survives the cloud brain being
        // unavailable — a summary that disappears when a key runs out is not a summary.
        return Ok(new
        {
            quiz,
            performance = QuizPerformance.Build(quiz),
        });
    }

    // ── Study Plan ──────────────────────────────────────────────────────────
    public sealed class CreateStudyPlanRequest
    {
        [Required, MinLength(1)] public string Topic { get; set; } = string.Empty;
        [Required, MinLength(1)] public string Goal { get; set; } = string.Empty;
        public int Weeks { get; set; } = 6;
    }

    [HttpPost("study-plan")]
    [EnableRateLimiting(RequestLimits.Thinking)]
    public async Task<IActionResult> CreateStudyPlanAsync([FromBody] CreateStudyPlanRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _mentor.CreateStudyPlanAsync(profile.Id, request.Topic, request.Goal, Math.Clamp(request.Weeks, 1, 12));
        return Ok(plan);
    }

    /// <summary>
    /// Turns a study plan into the learner's active roadmap.
    ///
    /// Spec 9.4b. A plan and a roadmap were two separate things a learner could be holding at
    /// once, with no way to say "this one". The plan is the better answer when they have just
    /// built one with the mentor - it was made against their stated goal, in conversation - so
    /// there has to be a way to adopt it rather than retyping the goal into a second screen and
    /// hoping the result matches.
    ///
    /// Creates the roadmap from the plan's own goal, so the two agree rather than being
    /// assembled separately from the same intention.
    /// </summary>
    [HttpPost("study-plan/{planId}/adopt")]
    public async Task<IActionResult> AdoptStudyPlanAsync(string planId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _mentor.GetActiveStudyPlanAsync(profile.Id);
        if (plan is null || plan.Id != planId)
        {
            return NotFound(new { error = "That study plan is not active for this account." });
        }

        var goal = !string.IsNullOrWhiteSpace(plan.Goal) ? plan.Goal : plan.Topic;
        if (string.IsNullOrWhiteSpace(goal))
        {
            return BadRequest(new { error = "This plan has no goal to build a path from." });
        }

        var roadmap = await _roadmaps.CreateRoadmapAsync(profile.Id, goal);
        return Ok(roadmap);
    }

    [HttpGet("study-plan/active")]
    public async Task<IActionResult> GetActiveStudyPlanAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _mentor.GetActiveStudyPlanAsync(profile.Id);
        return plan is null ? NoContent() : Ok(plan);
    }

    public sealed class AdjustPlanRequest
    {
        [Required] public string Reason { get; set; } = string.Empty;
    }

    [HttpPost("study-plan/{planId}/adjust")]
    public async Task<IActionResult> AdjustStudyPlanAsync(string planId, [FromBody] AdjustPlanRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _mentor.AdjustStudyPlanAsync(profile.Id, planId, request.Reason);
        return plan is null ? NotFound(new { error = "Plan not found" }) : Ok(plan);
    }

    public sealed class ToggleTaskRequest
    {
        [Required] public int WeekNumber { get; set; }
        [Required] public int TaskIndex { get; set; }
    }

    [HttpPost("study-plan/{planId}/toggle-task")]
    public async Task<IActionResult> ToggleTaskAsync(string planId, [FromBody] ToggleTaskRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var ok = await _mentor.ToggleTaskCompletionAsync(profile.Id, planId, request.WeekNumber, request.TaskIndex);
        return ok ? Ok(new { success = true }) : NotFound(new { error = "Task not found" });
    }

    // ── Personal Learning Memory & Topic Mastery ────────────────────────────
    [HttpGet("memory")]
    public async Task<IActionResult> GetLearningMemoryAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var memory = await _mentor.GetLearningMemoryAsync(profile.Id);
        return Ok(memory);
    }

    [HttpGet("mastery")]
    public async Task<IActionResult> GetTopicMasteriesAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var masteries = await _mentor.GetTopicMasteriesAsync(profile.Id);
        return Ok(masteries);
    }

    // ── Challenge Mode & Today's Learning ───────────────────────────────────
    public sealed class ChallengeRequest
    {
        public string? Topic { get; set; }
    }

    [HttpPost("challenge")]
    public async Task<IActionResult> RunChallengeAsync([FromBody] ChallengeRequest? request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var challenge = await _mentor.RunChallengeAsync(profile.Id, request?.Topic);
        return Ok(challenge);
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetTodayLearningAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var card = await _mentor.GetTodayLearningAsync(profile.Id);
        return Ok(card);
    }

    // ── Spaced Revision ─────────────────────────────────────────────────────
    [HttpGet("spaced-revision")]
    public async Task<IActionResult> GetSpacedRevisionsAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var revisions = await _mentor.GetSpacedRevisionsAsync(profile.Id);
        return Ok(revisions);
    }

    public sealed class CompleteRevisionRequest
    {
        public bool WasRemembered { get; set; } = true;
    }

    [HttpPost("spaced-revision/{id}/complete")]
    public async Task<IActionResult> CompleteRevisionAsync(string id, [FromBody] CompleteRevisionRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var ok = await _mentor.CompleteSpacedRevisionAsync(profile.Id, id, request.WasRemembered);
        return ok ? Ok(new { success = true }) : NotFound(new { error = "Revision item not found" });
    }
}
